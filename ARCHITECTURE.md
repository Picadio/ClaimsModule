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

FNOL accepts an optional `initialReserve` with `component` and `amount`.
The same reserve rules run inside the intake transaction. Submissions require
a linked policy. Reserve writes are disabled while the claim is Closed or
Withdrawn. POST and PUT support actor-, tenant- and claim-scoped request replay.

`amount` is a signed delta. An Add must start from zero with no pending Add.
Cost components cannot have negative balances and recovery cannot have a
positive balance. Adjust and Reverse require an existing nonzero approved
balance. Reverse must offset the entire balance. If a component is archived,
its type remains reserved on that claim. Transaction history retains the
before and projected after amounts at submission. A future approval must check
the operation against the then-current balance before applying its delta.

The absolute delta determines approval authority. The inclusive thresholds
are $10,000 for automatic approval and $100,000 for supervisors. A submission
that would raise the net approved total over $10 million records a warning and
stays pending until a manager override permits approval under the amount-based
authority rules. Only approved deltas update
balances, independently of posting. Pending amounts are the sum of pending
proposals. A pending opening cannot be bypassed by submitting another Add.

The component balance and sequence update in the same transaction as history,
audit and request replay. Reserve writes share the lifecycle claim lock. The
ledger reads balances and history in one short serializable transaction.

Each history row has a durable posting key derived from its reserve ID and
change sequence. A future GL dispatcher will select only approved rows with
pending posting. Manual approval, rejection, retraction, override and GL
execution are separate implementation stages.
