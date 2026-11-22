# ClaimsModule backend

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
| `GET /api/reference/claim-statuses` | List statuses and next steps available to the caller's role. Claim-specific checks run on writes. |
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
the claim has no assignment. The demo directory resolves that ID within the
configured organization. A loss outside the policy period requires
`acknowledgePolicyPeriodWarning: true` and records the acknowledgment in the
same transaction. Other FNOL warnings remain informational. Opening and closure
recheck the critical intake conditions against current data. FNOL rejects
critical input before persistence, so this slice has no stored critical issues.

`PendingPayment` requires an approved reserve. Coming from
`UnderInvestigation` also requires `liabilityDetermined: true`. Closure blocks
pending approvals and requires `confirmPositiveReserveBalance: true` plus a
reason when a component has a positive balance. These checks read persisted
reserve facts in the claim write transaction. A component with a nonzero
approved balance qualifies for `PendingPayment`.

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
