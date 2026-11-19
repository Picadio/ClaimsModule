using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoMapper;
using ClaimsModule.API.Security;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Claims;
using ClaimsModule.IntegrationTests.Security;
using ClaimsModule.Persistence.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClaimsModule.IntegrationTests.Claims;

public sealed class FnolApiTests(ClaimsApiFactory factory) : IClassFixture<ClaimsApiFactory>
{
    private const string ClaimsPath = "/api/claims";
    private const string SqlServerVariable = "CLAIMS_TEST_SQL_SERVER";
    private static readonly Guid SeededOrganization = Guid.Parse("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4");
    private static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task IntakeRequiresAuthentication()
    {
        using var client = factory.CreateApiClient();
        using var response = await client.PostAsJsonAsync(ClaimsPath, ValidRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IntakeRejectsSemanticErrorsBeforeAccessingSql()
    {
        using var client = factory.CreateApiClient();
        await Authenticate(client, factory);
        var request = ValidRequest();

        using var response = await client.PostAsJsonAsync(ClaimsPath, request with
        {
            Parties = [],
            LossEvent = request.LossEvent with { LossDescription = "Short", LossDate = DateTimeOffset.UtcNow.AddYears(1) }
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Parties", out _));
        Assert.True(errors.TryGetProperty("LossEvent.LossDate", out _));
        Assert.True(errors.TryGetProperty("LossEvent.LossDescription", out _));
    }

    [Fact]
    public void MapsTheClaimAndItsDetailsUsingTheRegisteredProfiles()
    {
        var mapper = factory.Services.GetRequiredService<IMapper>();
        mapper.ConfigurationProvider.AssertConfigurationIsValid();
        var request = ValidRequest();
        var reportedAt = DateTimeOffset.UtcNow;
        var claim = Claim.Report(factory.OrganizationId, factory.Account(UserRoles.Handler).UserId, reportedAt, 1,
            new FnolDetails(request.ClaimType, request.LossEvent, request.Parties, []), policy: null,
            causeOfLossIsActive: true);

        var detail = mapper.Map<ClaimDetail>(claim);
        var created = mapper.Map<CreatedClaim>(claim);

        Assert.Equal(reportedAt, detail.ReportedAt);
        Assert.Equal(request.LossEvent, detail.LossEvent);
        Assert.Equal(ClaimPartyRole.Claimant, Assert.Single(detail.Parties).Role);
        Assert.Equal(detail.ClaimNumber, created.ClaimNumber);
        Assert.Equal(detail.Warnings, created.Warnings);
    }

    [Fact]
    public void SqlServerModelTracksTheClaimGraphAndScopesReadsToTheOrganization()
    {
        var user = new UserIdentity(factory.Account(UserRoles.Handler).UserId, factory.OrganizationId, UserRoles.Handler);
        var options = new DbContextOptionsBuilder<ClaimsDbContext>().UseSqlServer().Options;
        using var context = new ClaimsDbContext(options, new TestCurrentUser(user));
        var request = ValidRequest();
        var claim = Claim.Report(user.OrganizationEntityId, user.UserId, DateTimeOffset.UtcNow, 1,
            new FnolDetails(request.ClaimType, request.LossEvent, request.Parties, []), policy: null,
            causeOfLossIsActive: true);

        var id = context.Add(claim).Property(row => row.Id);

        Assert.True(id.IsTemporary);
        Assert.Equal(id.CurrentValue, context.Entry(claim.LossEvent).Property(row => row.ClaimId).CurrentValue);
        Assert.Equal(id.CurrentValue, context.Entry(Assert.Single(claim.Parties)).Property(row => row.ClaimId).CurrentValue);

        var sql = context.Set<Claim>().ToQueryString();
        Assert.Contains("[OrganizationEntityId] =", sql);
        Assert.Contains(user.OrganizationEntityId.ToString("D"), sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[IsDeleted]", sql);
    }

    [SqlServerFact]
    public async Task CreatesAndReadsIsolatedClaimsWithGaplessNumbersAndIdempotency()
    {
        var database = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(SqlServerVariable))
        {
            InitialCatalog = $"ClaimsModuleFnol_{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<ClaimsDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var context = new ClaimsDbContext(options);
        try
        {
            await context.Database.MigrateAsync();
            await using var sqlApi = new ClaimsApiFactory(demoEnabled: true, organizationId: SeededOrganization,
                claimsConnection: database.ConnectionString);
            using var client = sqlApi.CreateApiClient();
            await Authenticate(client, sqlApi);
            var request = ValidRequest();

            using var invalid = await client.PostAsJsonAsync(ClaimsPath,
                request with { LossEvent = request.LossEvent with { CauseOfLossCode = "UNRECOGNIZED" } });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);

            using var firstRequest = await Post(client, request, "first-claim");
            var first = await ReadCreated(firstRequest);
            Assert.EndsWith("-0000001", first.ClaimNumber);
            Assert.Equal(ClaimStatus.Draft, first.Status);
            Assert.Contains(first.Warnings, warning => warning.Code == "POLICY_UNKNOWN");

            using var replayRequest = await Post(client, request, "first-claim");
            var replay = await ReadCreated(replayRequest);
            Assert.Equal(first.Id, replay.Id);
            Assert.Equal(first.ClaimNumber, replay.ClaimNumber);
            using var conflict = await Post(client, request with { ClaimType = "Vehicle" }, "first-claim");
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

            var concurrent = await Task.WhenAll(Post(client, request, "second-claim"), Post(client, request, "third-claim"));
            using var secondResponse = concurrent[0];
            using var thirdResponse = concurrent[1];
            var second = await ReadCreated(secondResponse);
            var third = await ReadCreated(thirdResponse);
            var suffixes = new[] { second.ClaimNumber[^7..], third.ClaimNumber[^7..] }.Order();
            Assert.Equal(new[] { "0000002", "0000003" }, suffixes);

            var read = await client.GetFromJsonAsync<ClaimDetail>(firstRequest.Headers.Location, ResponseJson);
            Assert.NotNull(read);
            Assert.Equal(first.Id, read.Id);
            Assert.Single(read.Parties);
            Assert.Equal(request.LossEvent.LossDescription, read.LossEvent.LossDescription);

            await using var otherApi = new ClaimsApiFactory(demoEnabled: true, claimsConnection: database.ConnectionString);
            using var otherClient = otherApi.CreateApiClient();
            await Authenticate(otherClient, otherApi);
            using var hidden = await otherClient.GetAsync(firstRequest.Headers.Location);
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

            var correlationId = Guid.Parse(Assert.Single(firstRequest.Headers.GetValues("X-Correlation-ID")));
            var auditRows = await context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM dbo.ClaimAuditLog
                WHERE ClaimId = {first.Id} AND CorrelationId = {correlationId}
                """).SingleAsync();
            Assert.Equal(3, auditRows);
            var update = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.ClaimAuditLog SET Description = 'changed' WHERE ClaimId = {first.Id}"));
            Assert.Equal(50002, update.Number);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static CreateClaimRequest ValidRequest() => new(null, "Property",
        new LossDetails(new DateTimeOffset(2025, 6, 1, 10, 0, 0, TimeSpan.Zero), "COL-FIRE",
            "A fire damaged the insured warehouse."),
        [new PartyDetails(ClaimPartyRole.Claimant, ClaimPartyType.Person, "Alex", "Taylor")]);

    private static async Task Authenticate(HttpClient client, ClaimsApiFactory host)
    {
        using var login = await client.PostAsJsonAsync("/api/auth/demo-token",
            new { username = UserRoles.Handler, password = host.Account(UserRoles.Handler).Password });
        login.EnsureSuccessStatusCode();
        var token = await login.Content.ReadFromJsonAsync<DemoTokenResponse>();
        Assert.NotNull(token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, CreateClaimRequest request, string key)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, ClaimsPath) { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
    }

    private static async Task<CreatedClaim> ReadCreated(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var claim = await response.Content.ReadFromJsonAsync<CreatedClaim>(ResponseJson);
        Assert.NotNull(claim);
        return claim;
    }

    private sealed class SqlServerFactAttribute : FactAttribute
    {
        public SqlServerFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerVariable)))
            {
                Skip = $"Set {SqlServerVariable} to a SQL Server connection with test database creation rights.";
            }
        }
    }

    private sealed record TestCurrentUser(UserIdentity Identity) : ICurrentUser;
}
