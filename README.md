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

Open **Create a demo account** from the Angular sign-in screen or use `POST /api/auth/register` in Swagger. Choose a username, a password of at least eight characters and one of the demo roles. Create a handler and a supervisor to test independent reserve approval, and a manager to test the third threshold. Login uses the configured demo tenant. Passwords are stored as salted hashes.

The migration retires the three original seeded logins and preserves their IDs and display names for reserve history. Existing JWTs expire according to their original lifetime. Take a database backup before the migration if you need to be able to restore the prior account state.

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
| `Authentication__Demo__AllowRegistration` | Set to `true` in the demo environment to allow self-registration and role selection. Enabled by default in Development. Keep disabled outside the demo. |
| `Authentication__Demo__OrganizationEntityId` | Tenant used to locate login accounts. Defaults to the seeded demo tenant. |
| `OpenApi__Enabled` | Set to `true` in Azure for public access to Swagger and OpenAPI. |
| `Claims__AllowDirectClosure` | Enables `Open → Closed`. Defaults to `false`. |
| `StorageProvider`, `ConnectionStrings__Blob` | In Azure, set `StorageProvider=AzureBlob` and configure the Blob connection so it can issue read SAS URLs. The `claim-documents` container must be private. |
| `Storage__LocalRoot` | Local document root. Defaults to `uploads` under the API content root. Development uses local storage when no Blob connection is configured. |

1. Provision separate Azure App Services for the API and Angular frontend, an Azure SQL database and a private Blob container named `claim-documents`. Configure the API settings above, including an SQL connection reachable from the API App Service. Store connection strings and the JWT signing key in deployment secrets or App Service settings.
2. Configure the GitHub Actions Azure OIDC identity and the App Service and resource group names in the [backend deployment workflow](.github/workflows/master_claimsmodule.yml). In GitHub Actions, select this workflow and use **Run workflow** for the revision to deploy. The workflow enables demo registration, publishes the API and applies EF migrations through the App Service WebJob. It then checks that the API responds successfully to `GET /api/health`. This endpoint checks process availability.
3. After applying migrations to a new database, enroll the non-owner API user in `ClaimsAuditWriter` using [ConfigureClaimsAuditRuntime.sql](deploy/ConfigureClaimsAuditRuntime.sql). Grant the runtime user the business-table permissions it needs, including `SELECT` and `INSERT` on `Users`. The audit role grants `SELECT` and `INSERT` on `ClaimAuditLog` and denies `UPDATE` and `DELETE`.
4. Set `CLAIMS_API_ORIGIN` on the frontend App Service, allow the exact frontend origin in the API App Service CORS settings and deploy the Angular app. The companion repository documents the frontend startup command and deployment settings. [Frontend repository](https://github.com/Picadio/ClaimsModule-frontend)

[Azure frontend](https://claimsmodule-frontend-hqdzhxe4cadhcvhn.spaincentral-01.azurewebsites.net/) · [Azure Swagger](https://claimsmodule-e3hshfhjh3csd3d6.spaincentral-01.azurewebsites.net/swagger/index.html) · [Azure OpenAPI](https://claimsmodule-e3hshfhjh3csd3d6.spaincentral-01.azurewebsites.net/openapi/v1.json)

## Demo and verification

Create a handler and supervisor with **Create a demo account** for the walkthrough. Create a manager to verify the highest approval tier. For the submission, share the reviewer accounts privately. Use synthetic claim details and files.

1. As `handler`, use `/api/policies/search?q=Meridian` to get the policy ID. In `POST /api/claims`, use that `policyId`, `claimType: "Property"`, a `lossEvent` on `2025-06-01T10:00:00Z` with `causeOfLossCode: "COL-FIRE"` and a `lossDescription` of at least 20 characters. Include a `Claimant` party of type `Person` with a first and last name. Expect `201` and `Draft`.
2. Open the claim via `PUT /api/claims/{id}/status` with `targetStatus: "Open"` and the handler's `userId` from `GET /api/auth/me` as `assignedHandlerId`. Query `GET /api/claims?assignedHandlerName=<name>` using the registered handler's username. Name matching is case-insensitive and partial. When supplied together, `assignedHandlerName` and `assignedHandlerId` must match the same handler.
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

The [Backend CI workflow](.github/workflows/ci.yml) starts SQL Server 2022 and runs the solution tests on each push. You can also run it manually from GitHub Actions. The tests cover migrations, tenant isolation, concurrent writes, Hangfire GL, and append-only audit permissions. To verify SLA on the deployed demo, use a dedicated test database with a synthetic Draft or Open claim idle for over 48 hours. Verify the 15-minute job writes `SLA_BREACH_DETECTED` and that a subsequent run within 24 hours adds no duplicate. Verify the Azure audit permissions using the deployed API's database identity. `SELECT` and `INSERT` should succeed on `ClaimAuditLog`. `UPDATE` and `DELETE` should be denied.
