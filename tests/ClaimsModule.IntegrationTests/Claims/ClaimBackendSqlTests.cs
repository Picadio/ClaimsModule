using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Claims.Documents;
using ClaimsModule.Application.Reserves;
using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.Infrastructure.Storage;
using ClaimsModule.IntegrationTests.Security;
using ClaimsModule.Persistence.Claims;
using MediatR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static ClaimsModule.IntegrationTests.Claims.ReserveApiTests;

namespace ClaimsModule.IntegrationTests.Claims;

public sealed class ClaimBackendSqlTests
{
    private static readonly Guid Organization = Guid.Parse("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4");
    private static readonly Guid Policy = Guid.Parse("1a46cfff-f909-44dc-ad15-000000000001");

    [SqlServerFact]
    public async Task PersistsSlaPartyDocumentAndOverviewChangesWithScopedAuditAndReplay()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(FnolApiTests.SqlServerVariable))
        {
            InitialCatalog = $"ClaimsModuleBackend_{Guid.NewGuid():N}"
        }.ConnectionString;
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var root = Path.Combine(temp, "claims-backend-" + Guid.NewGuid().ToString("N"));
        await using var db = new ClaimsDbContext(new DbContextOptionsBuilder<ClaimsDbContext>().UseSqlServer(connection).Options);
        try
        {
            await db.Database.MigrateAsync();
            await VerifyAuditPermissions(connection);
            await using var api = new ClaimsApiFactory(demoEnabled: true, claimsConnection: connection,
                organizationId: Organization);
            await using var testApi = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<IStorageService>(new LocalFileSystemStorageService(root))));
            using var client = testApi.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
            await FnolApiTests.Authenticate(client, api);

            var request = FnolApiTests.ValidRequest() with { Severity = ClaimSeverity.Critical };
            using var preview = await client.PostAsJsonAsync("/api/claims/validate",
                request with { PolicyId = Policy, InitialReserve = new(ReserveComponentType.Indemnity, 15_000m) }, JsonOptions);
            Assert.Equal(ReserveApprovalStatus.PendingApproval, (await Read<FnolValidationResult>(preview)).InitialReserveApprovalStatus);
            using var create = await SendJson(client, HttpMethod.Post, "/api/claims", request, "initial");
            var claim = await Read<CreatedClaim>(create);
            Assert.EndsWith("-0000001", claim.ClaimNumber);
            var path = $"/api/claims/{claim.Id:D}";

            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.Claims SET CreatedAt = {now.AddHours(-49)} WHERE OrganizationEntityId = {Organization} AND Id = {claim.Id}");
            var command = new DetectClaimSlaBreachCommand(claim.Id, now, Guid.NewGuid());
            Assert.False(await Detect(testApi.Services, Guid.NewGuid(), command));
            var breaches = await Task.WhenAll(Detect(testApi.Services, Organization, command),
                Detect(testApi.Services, Organization, command));
            Assert.Single(breaches, result => result);
            var detail = await GetDetail(client, path);
            Assert.True(detail.IsSlaBreached);
            Assert.Equal(ClaimSeverity.Critical, detail.Severity);
            var page = await client.GetFromJsonAsync<ClaimPage>("/api/claims", JsonOptions);
            Assert.NotNull(page);
            Assert.True(Assert.Single(page.Items).IsSlaBreached);
            await CheckParties(client, path, Assert.Single(detail.Parties).Id);
            Assert.False((await GetDetail(client, path)).IsSlaBreached);

            using var notes = await client.PutAsJsonAsync(path + "/notes", new UpdateClaimNotesRequest("Review next week"), JsonOptions);
            Assert.Equal("Review next week", (await Read<ClaimNotesResult>(notes)).Notes);
            using var policy = await client.PutAsJsonAsync(path + "/policy", new LinkClaimPolicyRequest(Policy), JsonOptions);
            Assert.DoesNotContain((await Read<ClaimPolicyResult>(policy)).Warnings, warning => warning.Code == ClaimWarning.UnknownPolicy);
            using var replay = await SendJson(client, HttpMethod.Post, "/api/claims", request, "initial");
            Assert.Contains((await Read<CreatedClaim>(replay)).Warnings, warning => warning.Code == ClaimWarning.UnknownPolicy);

            var document = await Upload(client, path, "Synthetic notes");
            var duplicate = await Upload(client, path, "Synthetic notes");
            Assert.Equal(document.Id, duplicate.Id);
            Assert.Equal("Loss_report.txt", document.FileName);
            Assert.NotNull(document.DownloadUrl);
            Assert.Equal("Synthetic notes", await client.GetStringAsync(document.DownloadUrl));
            Assert.Single((await GetDetail(client, path)).Documents);
            await using var other = new ClaimsApiFactory(demoEnabled: true, claimsConnection: connection);
            using var outsider = other.CreateApiClient();
            await FnolApiTests.Authenticate(outsider, other);
            using var forbidden = await outsider.GetAsync(document.DownloadUrl);
            Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);

            var audit = await client.GetFromJsonAsync<ClaimAuditPage>(path + "/audit?pageSize=100", JsonOptions);
            Assert.NotNull(audit);
            Assert.Single(audit.Items, item => item.EventType == "SLA_BREACH_DETECTED");
            Assert.Single(audit.Items, item => item.EventType == "PARTY_ADDED");
            Assert.Single(audit.Items, item => item.EventType == "PARTY_REMOVED");
            Assert.Single(audit.Items, item => item.EventType == "CLAIM_NOTES_UPDATED");
            Assert.Single(audit.Items, item => item.EventType == "DOCUMENT_UPLOADED" && item.RelatedEntityId == document.Id);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
            if (Path.GetDirectoryName(Path.GetFullPath(root)) == temp && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task<bool> Detect(IServiceProvider services, Guid organization, DetectClaimSlaBreachCommand command)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundTenant>().Initialize(organization);
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }

    private static async Task CheckParties(HttpClient client, string path, Guid initialPartyId)
    {
        var parties = path + "/parties";
        using var invalid = await client.DeleteAsync($"{parties}/{initialPartyId:D}");
        await AssertError(invalid, "PartyId");
        var input = new PartyDetails(ClaimPartyRole.Claimant, ClaimPartyType.Person, "Ada", "Gray");
        using var added = await SendJson(client, HttpMethod.Post, parties, input, "party");
        var party = await Read<ClaimPartyDto>(added);
        using var replay = await SendJson(client, HttpMethod.Post, parties, input, "party");
        Assert.Equal(party.Id, (await Read<ClaimPartyDto>(replay)).Id);
        using var removed = await client.DeleteAsync($"{parties}/{initialPartyId:D}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var last = await client.DeleteAsync($"{parties}/{party.Id:D}");
        await AssertError(last, "PartyId");
    }

    private static async Task<ClaimDetail> GetDetail(HttpClient client, string path) =>
        Assert.IsType<ClaimDetail>(await client.GetFromJsonAsync<ClaimDetail>(path, JsonOptions));

    private static async Task<ClaimDocumentDto> Upload(HttpClient client, string path, string content)
    {
        using var form = new MultipartFormDataContent();
        var file = new StringContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "Loss report.txt");
        form.Add(new StringContent("Other"), "documentType");
        using var request = new HttpRequestMessage(HttpMethod.Post, path + "/documents") { Content = form };
        request.Headers.Add("Idempotency-Key", "document");
        using var response = await client.SendAsync(request);
        return await Read<ClaimDocumentDto>(response);
    }

    private static async Task VerifyAuditPermissions(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE USER [claims_audit_spec] WITHOUT LOGIN;
            GRANT UPDATE, DELETE ON OBJECT::dbo.ClaimAuditLog TO [claims_audit_spec];
            GRANT UPDATE (Description) ON OBJECT::dbo.ClaimAuditLog TO [claims_audit_spec];
            ALTER ROLE [ClaimsAuditWriter] ADD MEMBER [claims_audit_spec];
            EXECUTE AS USER = 'claims_audit_spec';
            SELECT HAS_PERMS_BY_NAME('dbo.ClaimAuditLog', 'OBJECT', 'SELECT'),
                HAS_PERMS_BY_NAME('dbo.ClaimAuditLog', 'OBJECT', 'INSERT'),
                HAS_PERMS_BY_NAME('dbo.ClaimAuditLog', 'OBJECT', 'UPDATE'),
                HAS_PERMS_BY_NAME('dbo.ClaimAuditLog', 'OBJECT', 'DELETE'),
                HAS_PERMS_BY_NAME('dbo.ClaimAuditLog', 'OBJECT', 'UPDATE', 'Description', 'COLUMN');
            REVERT;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(new[] { 1, 1, 0, 0, 0 }, Enumerable.Range(0, 5).Select(reader.GetInt32));
    }
}
