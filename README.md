# ClaimsModule

A .NET 9 backend API for insurance claims management, covering First Notice of Loss (FNOL), claim processing, reserve approvals, simulated general ledger (GL) posting, and audit history.

## Live demo

- [Angular web application](https://claimsmodule-frontend-hqdzhxe4cadhcvhn.spaincentral-01.azurewebsites.net/)
- [Swagger UI](https://claimsmodule-e3hshfhjh3csd3d6.spaincentral-01.azurewebsites.net/swagger/index.html)
- [OpenAPI specification](https://claimsmodule-e3hshfhjh3csd3d6.spaincentral-01.azurewebsites.net/openapi/v1.json)
- [Frontend repository](https://github.com/Picadio/ClaimsModule-frontend)

## Features

- First Notice of Loss (FNOL), policy search, and claim party management.
- Claim lifecycle, handler assignment, status transitions, and closure validation.
- Role-based reserve approval workflows and simulated GL posting.
- Background jobs for GL posting and SLA monitoring.
- Document upload and download using local storage or Azure Blob Storage.
- Tenant isolation, JWT authentication, idempotent writes, and append-only audit history.

## Technology stack

| Area | Technology |
| --- | --- |
| Backend | .NET 9, ASP.NET Core Web API |
| Persistence | Entity Framework Core 9, SQL Server 2022 / Azure SQL |
| Background processing | Hangfire |
| Authentication | JWT |
| API documentation | Swagger / OpenAPI |
| Frontend | Angular (separate repository) |
| Hosting and storage | Azure App Service, Azure Blob Storage |
| CI/CD | GitHub Actions |

## Getting started

### Prerequisites

- .NET 9 SDK
- SQL Server 2022 or Azure SQL (an accessible database server)
- PowerShell 7

Run the following commands from the repository root.

### 1. Configure the database connection

Set the connection string for your existing SQL Server or Azure SQL instance. Replace the placeholders with your database credentials. For local development, the following example uses SQL authentication and a trusted server certificate override:

```powershell
$env:ConnectionStrings__Claims = 'Server=localhost,1433;Database=ClaimsModule;User ID=<username>;Password=<password>;Encrypt=True;TrustServerCertificate=True'
```

For Azure SQL, use the appropriate server name and certificate validation settings. Do not commit credentials to the repository.

### 2. Apply database migrations

```powershell
dotnet tool install dotnet-ef --global --version 9.0.23
dotnet ef database update --project src/ClaimsModule.Persistence --startup-project src/ClaimsModule.API --context ClaimsDbContext
```

Skip the tool installation if the required version is already installed. Migrations create the schema and seed policies and cause codes for the tenant configured in [appsettings.json](src/ClaimsModule.API/appsettings.json).

When upgrading an existing database, note that the migration retires the three original seeded logins while preserving their IDs and display names for reserve history. Existing JWTs expire according to their original lifetime. Back up the database if you need to restore the previous account state.

### 3. Start the API

```powershell
dotnet dev-certs https --trust
dotnet run --project src/ClaimsModule.API --launch-profile https
```

- [Local Swagger UI](https://localhost:7273/swagger/index.html)
- [Local OpenAPI specification](https://localhost:7273/openapi/v1.json)

### 4. Register and authenticate

Create an account from the Angular sign-in screen using **Create a demo account**, or call `POST /api/auth/register` in Swagger. Choose a username, a password of at least eight characters, and a demo role (`handler`, `supervisor`, or `manager`). Passwords are stored as salted hashes.

Call `POST /api/auth/demo-token` with your username and password, then authorize Swagger using the returned `accessToken`. Use `GET /api/auth/me` to retrieve the current user and organization IDs.

Development enables demo login and generates a JWT signing key at startup if none is configured. Restarting the API invalidates existing tokens when a generated key is used. Login uses the configured demo tenant.

## Configuration

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
| `Claims__AllowDirectClosure` | Enables `Open to Closed`. Defaults to `false`. |
| `StorageProvider`, `ConnectionStrings__Blob` | In Azure, set `StorageProvider=AzureBlob` and configure the Blob connection so it can issue read SAS URLs. The `claim-documents` container must be private. |
| `Storage__LocalRoot` | Local document root. Defaults to `uploads` under the API content root. Development uses local storage when no Blob connection is configured. |

## Azure deployment

1. Provision separate Azure App Services for the API and Angular frontend, an Azure SQL database and a private Blob container named `claim-documents`. Configure the API settings above, including an SQL connection reachable from the API App Service. Store connection strings and the JWT signing key in deployment secrets or App Service settings.
2. Configure the GitHub Actions Azure OIDC identity and the App Service and resource group names in the [backend deployment workflow](.github/workflows/master_claimsmodule.yml). In GitHub Actions, select this workflow and use **Run workflow** for the revision to deploy. The workflow enables demo registration, publishes the API and applies EF migrations through the App Service WebJob. It then checks that the API responds successfully to `GET /api/health`. This endpoint checks process availability.
3. After applying migrations to a new database, enroll the non-owner API user in `ClaimsAuditWriter` using [ConfigureClaimsAuditRuntime.sql](deploy/ConfigureClaimsAuditRuntime.sql). Grant the runtime user the business-table permissions it needs, including `SELECT` and `INSERT` on `Users`. The audit role grants `SELECT` and `INSERT` on `ClaimAuditLog` and denies `UPDATE` and `DELETE`.
4. Set `CLAIMS_API_ORIGIN` on the frontend App Service, allow the exact frontend origin in the API App Service CORS settings and deploy the Angular app. The companion repository documents the frontend startup command and deployment settings. [Frontend repository](https://github.com/Picadio/ClaimsModule-frontend)

## Demo walkthrough

Create a handler and supervisor with **Create a demo account** for the walkthrough. Create a manager to verify the highest approval tier. Share reviewer credentials privately if needed. Use synthetic claim details and files.

## Testing

```powershell
dotnet test ClaimsModule.sln

# Optional local SQL Server suite. The test user needs to create and drop test databases.
$env:CLAIMS_TEST_SQL_SERVER = 'Server=127.0.0.1,1433;Database=master;User ID=<test user>;Password=<password>;Encrypt=True;TrustServerCertificate=True'
dotnet test ClaimsModule.sln
```

The [Backend CI workflow](.github/workflows/ci.yml) starts SQL Server 2022 and runs the solution tests on each push. You can also run it manually from GitHub Actions. The tests cover migrations, tenant isolation, concurrent writes, Hangfire GL, and append-only audit permissions.

## Documentation

- [Architecture](ARCHITECTURE.md) - system design and architectural details.
- [AI workflow](AI-WORKFLOW.md) - development history.
- [Backend CI workflow](.github/workflows/ci.yml) - automated tests.
- [Backend deployment workflow](.github/workflows/master_claimsmodule.yml) - Azure deployment.
