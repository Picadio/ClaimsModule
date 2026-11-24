# ClaimsModule backend

## Solution structure

The Angular application is in a separate repository and calls the ASP.NET Core API over HTTPS. The API validates the Bearer token and resolves the caller's tenant, user ID and role. The five .NET projects in `ClaimsModule.sln` have these responsibilities and direct dependencies.

| Project | Responsibility | Project dependencies |
| --- | --- | --- |
| Domain | Claim and reserve aggregates, state transitions, invariants and domain events | None |
| Application | MediatR commands and queries, FluentValidation, DTOs, AutoMapper profiles and application interfaces | Domain |
| Persistence | EF Core mappings and migrations, SQL stores, transactions and audit persistence | Application |
| Infrastructure | Hangfire scheduling and dispatch, Azure Blob Storage and local file storage | Application |
| API | HTTP contracts, JWT, error responses, request correlation and dependency injection | Application, Persistence, Infrastructure |

## Data model

| Entity | Relationships and purpose |
| --- | --- |
| Claim | Owns one loss event and collections of parties, risk objects and intake warnings. It can link to one simulated policy. |
| Loss event and reference data | The loss event refers to a tenant's cause-of-loss code. Policies provide coverage dates and coverage types. Status transitions are seeded reference data. |
| Reserve component and history | A claim can have multiple reserve components. Each component has a history of adjustments and approval decisions. A revision can refer to a rejected history row. |
| Document and audit entry | A claim can have multiple documents and audit entries. SQL holds document metadata and the storage key. Audit entries can reference a related entity and carry the actor and correlation ID. |
| User | SQL stores demo accounts, tenant scope and role. Claims record the assigned handler ID and reserve history records the submitter and approver IDs. |

Tenant-scoped EF filters apply to business rows and exclude soft-deleted records. Tenant keys participate in relationships such as claim to loss event and reserve component to history. Claims and reserve components have row versions. A transaction allocates claim numbers from a counter scoped by organization and year. Financial amounts use SQL `decimal(19,4)`.

## CQRS and domain events

For FNOL, the controller sends a `CreateClaimCommand` through the MediatR validation pipeline. The handler resolves the caller and policy, runs the domain rules and saves the claim through `IClaimStore` and `IClaimUnitOfWork`. The SQL transaction covers the claim number, the claim and its children, audit rows, any initial reserve and the idempotency record. Later changes use a lock scoped to the tenant and claim. Queries such as `GetClaimQuery` use tenant-scoped stores to assemble the API response.

The claim raises `ClaimCreated`. The Application layer publishes `ClaimCreatedNotification` and its handler records the creation and warning audit entries in the intake transaction. The claim status change notification records transitions in the status transaction. Reserve submission, decision and override notifications record financial audit events. After a reserve is approved and its transaction commits, `ReservePostingReady` requests Hangfire dispatch. The recovery job finds eligible postings if dispatch fails. The job design and duplicate detection are described below.

## Azure deployment

- The Angular App Service serves the SPA. Its startup config supplies the API origin. Browser requests use HTTPS and the API App Service CORS policy allows the frontend origin.
- The API runs on a Linux App Service. It uses Azure SQL for claims and Hangfire storage. The demo API App Service connects to Azure SQL through VNet integration and the database's private endpoint.
- The API stores files in the private `claim-documents` Azure Blob container. It issues time-limited read SAS URLs after checking claim access, and the browser downloads directly from Blob Storage.
- GitHub Actions runs the SQL-backed backend tests on push. The manually triggered backend deployment publishes the API and then runs the EF migration bundle as a WebJob inside the API App Service, where it can reach Azure SQL. The workflow waits for the migration result before checking `GET /api/health`. Angular builds and deploys through the companion repository's workflow. [Backend workflow](.github/workflows/master_claimsmodule.yml) · [Frontend repository](https://github.com/Picadio/ClaimsModule-frontend)

## Claim lifecycle and audit

The API exposes the lifecycle through MediatR commands and queries. Persistence
seeds the shared `ClaimStatusTransitions` reference table in an EF migration.
The command checks the current status and the caller's role against that table,
then applies the claim's business rules.
An invalid transition gets HTTP 422 with the next statuses available to the
caller, and a failed closure returns all blocking conditions.

| Endpoint | Purpose |
| --- | --- |
| `PUT /api/claims/{id}/status` | Apply a transition using `targetStatus` and optional `reason`. Accepts `Idempotency-Key`. |
| `GET /api/reference/claim-statuses` | List statuses and next steps available to the caller's role. |
| `GET /api/reference/users` | List active users in the caller's organization by ID, display name and role. Supports handler assignment and actor labels. |
| `POST /api/auth/register` | Create an account in the configured demo tenant when demo login and registration are both enabled. The selected demo role is validated and an optional `Idempotency-Key` supports replay. |
| `GET /api/claims/{id}/closure-check` | Read the claim-specific checklist for closing. Returns blockers and indicates when a positive balance needs confirmation. |
| `GET /api/claims?assignedHandlerName=hand` | Filter claims by a partial, case-insensitive name from the tenant's demo handler directory. |
| `GET /api/claims/{id}/audit?page=1&pageSize=20` | Return audit entries newest first. Page sizes range from 1 to 100. |
| `GET /api/claims/{id}` | Include assignment, closure metadata and up to ten recent audit entries. |

The Candidate Specification describes the user transitions as handler actions
and specifically requires a supervisor for reopening. The seeded permissions
use that interpretation, with managers inheriting supervisor rights. Reopening
records `Closed → Reopened` and `Reopened → Open` within one transaction. The
response reflects the final state. The Assessment's optional `Open → Closed`
shortcut can be enabled with `Claims__AllowDirectClosure=true` and uses the same
closure checks as the other routes. It is disabled by default.

When opening a Draft, pass the configured handler's `assignedHandlerId` if
the claim has no assignment. The handler directory resolves that ID from the
tenant-filtered Users table. A loss outside the policy period requires
`acknowledgePolicyPeriodWarning: true` and records the acknowledgment in the
same transaction. Other FNOL warnings remain informational. Opening and closure
recheck the critical intake conditions against current data. FNOL rejects
critical input before persistence, so this slice has no stored critical issues.
Handler-name matching resolves user display names to assignment IDs before
applying SQL pagination. An explicit `assignedHandlerId`
also has to match when supplied.

`PendingPayment` requires an approved reserve. Coming from
`UnderInvestigation` also requires `liabilityDetermined: true`. Closure blocks
pending approvals and requires `confirmPositiveReserveBalance: true` plus a
reason when a component has a positive balance. These checks read persisted
reserve facts in the claim write transaction. A component with a nonzero
approved balance qualifies for `PendingPayment`.

The closure checklist reads claim and reserve facts in a short serializable
transaction. It takes a shared claim lock before reading so concurrent edits
and the checklist cannot hold conflicting row locks in opposite orders. An
in-progress edit can delay the checklist until it releases the claim lock.
It uses the same transition policy and domain rules as the write.
`canClose` means no current blockers, and a positive component balance still
requires explicit confirmation and a reason in the write. The command checks
the rules against the latest data in its own transaction.

The Unit of Work takes a transaction-owned write lock per tenant and claim.
Future party and reserve commands should use this lock when updating facts the
lifecycle reads. Claim changes, audit entries and the idempotent result commit
together. A reused key is scoped to the tenant, claim and actor. A different
payload under that key returns HTTP 409.

`IAuditLogService` appends entries with the acting user, UTC time,
`X-Correlation-ID`, structured old/new values and related entity information
where applicable. A SQL trigger enforces append-only writes. Audit reads use
the tenant and soft-delete filters on both the claim and its audit records.
Each audit page and its count are read in the same short serializable
transaction. Within a transition, entries sharing a timestamp have a persisted
order.

## Financial reserves

| Endpoint | Purpose |
| --- | --- |
| `POST /api/claims/{id}/reserves` | Submit `component`, `transactionType`, `amount` and `changeReason`. Add opens a zero-balance component. Adjust and Reverse target an existing component by type. |
| `PUT /api/claims/{id}/reserves/{reserveId}` | Submit an Adjust or Reverse against the selected component. |
| `GET /api/claims/{id}/reserves` | Read approved and pending amounts per component with their transaction history. |
| `POST /api/claims/{id}/reserves/{txnId}/approve` | Approve a pending transaction using the caller's authority and current balance. |
| `POST /api/claims/{id}/reserves/{txnId}/reject` | Reject a pending transaction and record `rejectionReason`. |
| `POST /api/claims/{id}/reserves/{txnId}/retract` | Cancel a pending transaction as its submitter. |
| `PUT /api/claims/{id}/reserves/manager-override` | Enable the claim's aggregate reserve limit override as a manager and record `reason`. |

FNOL accepts an optional `initialReserve` with `component` and `amount`.
The same reserve rules run inside the intake transaction. Submissions and
approvals require a linked policy and a claim outside Closed or Withdrawn.
POST and PUT support actor-, tenant- and claim-scoped request replay.

`amount` is a signed delta. An Add must start from zero with no pending Add.
Cost components cannot have negative balances and recovery cannot have a
positive balance. Adjust and Reverse require an existing nonzero approved
balance. Reverse must offset the entire balance. If a component is archived,
its type remains reserved on that claim. Transaction history retains the
before and projected after amounts at submission. Approval checks the
operation against the current balance before applying its delta. The approval
audit records the actual before and after amounts.

The absolute delta determines approval authority. The inclusive thresholds
are $10,000 for automatic approval and $100,000 for supervisors. A submission
that would raise the net approved total over $10 million records a warning and
stays pending until a manager override permits approval under the amount-based
authority rules. Only approved deltas update
balances, independently of posting. Pending amounts are the sum of pending
proposals. A pending opening cannot be bypassed by submitting another Add.
The transaction's override warning describes the projection at submission.
The approval command rechecks the current net total inside the claim lock.

Approve and reject use the transaction identifier from the Candidate
Specification. The Assessment uses the label `reserveId` for those actions.
Using the transaction identifier lets the caller select one pending adjustment
when the component has several. Supervisors can approve or reject up to
$100,000 per operation. Managers can review above that amount. The
Assessment's approval tiers have no separate manager ceiling, so the $10
million ceiling applies to the claim's signed total. The override can be
enabled once per claim and records the manager's identity, time and reason.
A manager still cannot approve their own transaction. The ledger reports
whether the flag is enabled.

To revise a rejected operation, submit a new transaction with
`replacesRejectedTransactionId`. It must belong to the original submitter and
have the same component and transaction type. The revision remains pending
even below the auto-approval threshold. Only one uncancelled revision can
replace a rejected transaction. After a rejection, use the latest rejected
record for a further revision. After retraction, the same source can be revised
again. Rejection and retraction retain the
original financial fields and cancel posting eligibility. Cleanup remains
available for withdrawn claims and claims whose policy has been archived.

The component balance and sequence update in the same transaction as history,
audit and request replay. Reserve writes share the lifecycle claim lock. The
ledger reads balances and history in one short serializable transaction.

## GL posting simulation

An auto-approved or manually approved reserve with pending posting is a
durable dispatch record. This includes an automatically approved initial FNOL
reserve. The API attempts to enqueue after its database transaction commits.
The recovery service checks one page of approved pending rows per minute
in each configured organization, starting at startup. Each organization
has its own cursor and each sweep has a fixed upper ID, so newly submitted
work cannot indefinitely delay a revisit. Recovery handles missing dispatches
and terminal Hangfire failures. Previously approved pending reserves are
eligible.

Hangfire executes the posting with its automatic retry policy. A claim write
lock serializes assignment and execution. A worker must match the saved
Hangfire job ID and the business key `Reserve:{ReserveId}:Change:{ChangeSequence}`.
The status and audit row commit together. Duplicate or obsolete jobs cannot
write a second posting. A positive delta debits Change in Outstanding Reserves
and credits Outstanding Loss Reserves. A negative delta swaps the accounts and
records the absolute amount.

When Hangfire reaches terminal failure, recovery sets `Failed` and records
`GL_POSTING_FAILED`. An authenticated user can call
`POST /api/claims/{id}/reserves/{txnId}/retry-posting` on a failed approved
transaction. The retry retains the business key and gets a new Hangfire job
ID. The workflow carries the approval or retry correlation ID into the audit
and leaves the approved balance and claim activity timestamp unchanged.

Jobs run when `ConnectionStrings__Claims` is configured. Set
`Jobs__Enabled=false` to disable the worker. Hangfire uses the claims database
by default and can use `ConnectionStrings__Hangfire` for its own SQL storage.
Its SQL login needs permission to initialize the Hangfire schema. Configure
`Jobs__OrganizationEntityIds__0` and additional indexed values for each
organization that needs recovery. The demo organization is the default.
Keep the API host running for background processing. [Hangfire SQL storage](https://docs.hangfire.io/en/latest/configuration/using-sql-server.html)

## SLA monitoring

Hangfire scans each configured organization every 15 minutes. A Draft or Open
claim is eligible strictly more than 48 hours after its last business activity,
using creation time until the first update. The claim lock serializes checks,
and the last breach marker and append-only audit commit together. The same
claim produces at most one breach event per 24 hours. The API reports the
active flag and last recorded time separately from lifecycle status. Business
activity clears the active flag until a new breach is recorded.

## Parties and attachments

POST and DELETE under `/api/claims/{id}/parties` add and deactivate parties.
The claim lock serializes deactivations so one active Claimant always remains.
The active and inactive parties remain visible in claim detail.

`POST /api/claims/{id}/documents` accepts one multipart `file` with
`documentType` and optional `notes`. The API checks the extension, media type
and file signature or text encoding up to 50 MiB. It stores a unique,
sanitized file key under the organization's claim path. Metadata, upload
audit and the idempotency receipt commit together. A repeated request returns
the same metadata with a newly issued link. If the database write fails
before commit, the API attempts to remove the file. If Azure loses its
upload response, cleanup checks the request's ownership token and uses an
ETag condition before deleting. An ambiguous SQL commit leaves the file
intact so a committed record can still download it.

Set `StorageProvider=AzureBlob` and `ConnectionStrings__Blob` to use the
private `claim-documents` container and HTTPS read SAS links valid for one hour.
Disable anonymous blob access at the storage account. The browser downloads
directly from Azure using the link returned by GET documents or claim detail.
Local development defaults to `LocalFileSystem` under `uploads`, configurable
via `Storage__LocalRoot`. Local links use the authenticated API and should be
fetched with the caller's Bearer token. [Azure service SAS](https://learn.microsoft.com/en-us/azure/storage/blobs/sas-service-create)

## Claim overview and intake validation

FNOL accepts optional severity and uses Standard when omitted. The detail API
returns severity and notes. `PUT /api/claims/{id}/notes` replaces notes, with
`null` clearing them. `PUT /api/claims/{id}/policy` links an unlinked claim to a
visible policy in the same organization. It resolves the unknown-policy warning
and applies the intake date rule. If a claim has already left Draft, the caller
must acknowledge an outside-policy-period warning in the link request.

`POST /api/claims/validate` runs the same request validation, policy and cause
lookups, and initial reserve rules used for intake. It returns warnings and
the initial reserve approval preview. Validation uses no claim sequence and
creates no records.

## Audit database permissions

The migrations create the `ClaimsAuditWriter` database role with SELECT and
INSERT access and explicit denial of UPDATE and DELETE on ClaimAuditLog.
The UPDATE denial applies to each column as well. Migrations adding audit
columns must extend the column-level denial.
The append-only trigger remains active. Configure the API's `Claims` connection
with an existing non-owner runtime database principal and enroll it using
`deploy/ConfigureClaimsAuditRuntime.sql`. Keep the migration administrator
connection in `ClaimsMigrations`. The audit role supplements the runtime
principal's permissions on the claim and job tables.
