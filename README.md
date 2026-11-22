# ClaimsModule

ClaimsModule is a .NET 9 API for First Notice of Loss (FNOL), claim lifecycle and parties, reserve approvals, simulated general ledger posting, SLA monitoring, documents, and audit history. See [ARCHITECTURE.md](ARCHITECTURE.md) for the design and [AI-WORKFLOW.md](AI-WORKFLOW.md) for development history.

## Run locally

Install the .NET 9 SDK and use SQL Server 2022 or Azure SQL. Run these commands from the repository root in PowerShell 7. For an existing SQL Server, skip the Docker command and set `ConnectionStrings__Claims` to your connection string.

```powershell
$env:MSSQL_SA_PASSWORD = Read-Host 'Local SQL Server password' -MaskInput
docker run --detach --name claims-sql --publish 127.0.0.1:1433:1433 `
  --env ACCEPT_EULA=Y --env MSSQL_PID=Developer --env MSSQL_SA_PASSWORD `
  mcr.microsoft.com/mssql/server:2022-latest
$claimsSql = [System.Data.Common.DbConnectionStringBuilder]::new()
$claimsSql.set_ConnectionString('Server=127.0.0.1,1433;Database=ClaimsModule;User ID=sa;Encrypt=True;TrustServerCertificate=True')
$claimsSql.set_Item('Password', $env:MSSQL_SA_PASSWORD)
$env:ConnectionStrings__Claims = $claimsSql.get_ConnectionString()
```

Wait for SQL Server to accept connections. The migrations create the schema and seed policies and cause codes for the tenant configured in [appsettings.json](src/ClaimsModule.API/appsettings.json).

```powershell
dotnet tool install dotnet-ef --global --version 9.0.23
dotnet ef database update --project src/ClaimsModule.Persistence --startup-project src/ClaimsModule.API --context ClaimsDbContext

dotnet dev-certs https --trust
dotnet run --project src/ClaimsModule.API --launch-profile https
```

Open [local Swagger](https://localhost:7273/swagger/index.html) or the [local OpenAPI document](https://localhost:7273/openapi/v1.json). In Swagger, use `POST /api/auth/demo-token` with `username` and `password`, then authorize with the response's `accessToken`. `GET /api/auth/me` returns the current user and organization IDs. Development enables demo login and generates a JWT signing key at startup if none is configured. With that generated key, restarting the API invalidates existing tokens.

The migration seeds three demo users named `handler`, `supervisor`, and `manager`, each with password `1`. The database stores salted password hashes. Demo login uses the configured `Authentication__Demo__OrganizationEntityId` to select the tenant. The former `Authentication__Demo__Accounts__*` settings no longer control account IDs or passwords.

If the previous deployment used custom `Authentication__Demo__Accounts__<role>__UserId` values, retain those settings during migration. Before enabling the new login, an administrator must update the corresponding seeded `Users.Id` values to the prior actor IDs and verify existing claim assignments and reserve ownership. Then remove the legacy ID overrides. The application rejects mismatched overrides at startup while demo login is enabled. If overrides were removed before upgrading, compare historical actor IDs before enabling login, since the application cannot detect removed settings.

## Configuration and deployment

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings__Claims` | Runtime SQL connection. Use a dedicated database user in Azure. |
| `ConnectionStrings__ClaimsMigrations` | Optional separate connection for the Azure migration WebJob. For local EF commands, set `ConnectionStrings__Claims` to the migration connection for that invocation. |
| `ConnectionStrings__Hangfire` | Optional separate Hangfire SQL connection. Defaults to the claims connection. The connection must be able to initialize and use the Hangfire schema. |
| `Jobs__Enabled` | Enables GL posting and SLA monitoring. Defaults to enabled when a claims connection is configured. |
| `Jobs__OrganizationEntityIds__0` | First tenant serviced by background jobs. Defaults to the configured demo tenant when the list is absent. |
| `Authentication__Jwt__SigningKey` | Required outside Development. Use a Base64-encoded value of at least 32 random bytes. |
| `Authentication__Demo__Enabled` | Set to `true` for the deployed demo. |
| `Authentication__Demo__OrganizationEntityId` | Tenant used to locate login accounts. Defaults to the seeded demo tenant. |
| `OpenApi__Enabled` | Set to `true` in Azure for public access to Swagger and OpenAPI. |
| `Claims__AllowDirectClosure` | Enables `Open → Closed`. Defaults to `false`. |
| `StorageProvider`, `ConnectionStrings__Blob` | In Azure, set `StorageProvider=AzureBlob` and configure the Blob connection so it can issue read SAS URLs. The `claim-documents` container must be private. |
| `Storage__LocalRoot` | Local document root. Defaults to `uploads` under the API content root. Development uses local storage when no Blob connection is configured. |

The [Azure workflow](.github/workflows/master_claimsmodule.yml) publishes the API and applies its EF migration bundle in App Service. After migration, use [ConfigureClaimsAuditRuntime.sql](deploy/ConfigureClaimsAuditRuntime.sql) to enroll the non-owner API user in `ClaimsAuditWriter`. Give that user the permissions needed to operate the business tables, including SELECT on Users. The audit role grants `SELECT` and `INSERT` on `ClaimAuditLog` and denies `UPDATE` and `DELETE`. Store connection strings and the JWT signing key in deployment secrets or App Service settings.

[Azure Swagger](https://claimsmodule-e3hshfhjh3csd3d6.spaincentral-01.azurewebsites.net/swagger/index.html) · [Azure OpenAPI](https://claimsmodule-e3hshfhjh3csd3d6.spaincentral-01.azurewebsites.net/openapi/v1.json)

## Demo and verification

Run this walkthrough against a demo database with the configured tenant and three demo accounts. Use synthetic claim details and files.

1. As `handler`, use `/api/policies/search?q=Meridian` to get the policy ID. In `POST /api/claims`, use that `policyId`, `claimType: "Property"`, a `lossEvent` on `2025-06-01T10:00:00Z` with `causeOfLossCode: "COL-FIRE"` and a `lossDescription` of at least 20 characters. Include a `Claimant` party of type `Person` with a first and last name. Expect `201` and `Draft`.
2. Open the claim via `PUT /api/claims/{id}/status` with `targetStatus: "Open"` and the handler's `userId` from `GET /api/auth/me` as `assignedHandlerId`. Query `GET /api/claims?assignedHandlerName=hand` using the default name. Name matching is case-insensitive and partial. When supplied together, `assignedHandlerName` and `assignedHandlerId` must match the same handler.
3. As `handler`, submit an `Indemnity` reserve using `POST /api/claims/{id}/reserves` with `amount: 15000` and a `changeReason`. Expect `PendingApproval`. As `supervisor`, use `POST /api/claims/{id}/reserves/{txnId}/approve` with the transaction ID from submission. With Hangfire enabled, the ledger at `GET /api/claims/{id}/reserves` eventually shows `postingStatus: "Posted"`.
4. Upload a small `.txt` file as multipart `file` to `POST /api/claims/{id}/documents`. Get `downloadUrl` from the document list. In Azure, use the one-hour read-only SAS URL to download directly from Blob Storage. With local storage, use your Bearer token to fetch the API download path. Check the claim audit for `RESERVE_APPROVED`, one `GL_POSTING_SIMULATED` for the transaction, and `DOCUMENT_UPLOADED`.
5. Move the claim to `UnderInvestigation` and call `GET /api/claims/{id}/closure-check`. `transitionAllowed` reflects status, permissions, and configuration. `blockers` groups validation messages by field. `canClose` means the checklist found no blockers. If `requiresPositiveReserveConfirmation` is true, send `confirmPositiveReserveBalance: true` and a nonblank `reason` along with `targetStatus: "Closed"`. The update checks eligibility again against the current data.

If retrying a write, reuse the same `Idempotency-Key` with the same payload. Use a new key when changing the payload.

```powershell
dotnet test ClaimsModule.sln

# Optional local SQL Server suite. The test user needs to create and drop test databases.
$env:CLAIMS_TEST_SQL_SERVER = 'Server=127.0.0.1,1433;Database=master;User ID=<test user>;Password=<password>;Encrypt=True;TrustServerCertificate=True'
dotnet test ClaimsModule.sln
```

The [Backend CI workflow](.github/workflows/ci.yml) starts SQL Server 2022 for the SQL-backed tests on push. Those tests cover migrations, tenant isolation, concurrent writes, Hangfire GL, and append-only audit permissions. To verify SLA on the deployed demo, use a dedicated test database with a synthetic Draft or Open claim idle for over 48 hours. Verify the 15-minute job writes `SLA_BREACH_DETECTED` and that a subsequent run within 24 hours adds no duplicate. Verify the Azure audit permissions using the deployed API's database identity. `SELECT` and `INSERT` should succeed on `ClaimAuditLog`. `UPDATE` and `DELETE` should be denied.
