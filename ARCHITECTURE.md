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
reason when a component has a positive balance. The current schema has no
reserve transactions. `ReadReserveFactsAsync` therefore returns empty facts,
and `PendingPayment` stays blocked until the reserve module supplies them.

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
