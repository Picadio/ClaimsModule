using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoMapper;
using ClaimsModule.API.Security;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Reference;
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
    internal const string SqlServerVariable = "CLAIMS_TEST_SQL_SERVER";
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

    [Theory]
    [InlineData("/api/claims")]
    [InlineData("/api/policies/search?q=Meridian")]
    [InlineData("/api/policies/1a46cfff-f909-44dc-ad15-000000000001/coverage")]
    [InlineData("/api/reference/cause-of-loss-codes")]
    [InlineData("/api/reference/users")]
    public async Task ClaimsAndReferenceReadsRequireAuthentication(string path)
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/claims?page=0", "Page")]
    [InlineData("/api/claims?status=Draft&status=Open&pageSize=101", "PageSize")]
    [InlineData("/api/claims?page=2147483647", "Page")]
    [InlineData("/api/claims?dateFrom=2025-06-02&dateTo=2025-06-01", "DateTo")]
    public async Task ClaimListRejectsInvalidFiltersBeforeQueryingSql(string path, string invalidField)
    {
        using var client = factory.CreateApiClient();
        await Authenticate(client, factory);
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(invalidField, out _));
    }

    [Theory]
    [InlineData("assignedHandlerName=Unmatched")]
    [InlineData("assignedHandlerName=hAnD&assignedHandlerId=00000000-0000-0000-0000-000000000001")]
    public async Task ClaimListWithNoMatchingHandlerReturnsRequestedEmptyPage(string filter)
    {
        using var client = factory.CreateApiClient();
        await Authenticate(client, factory);
        var page = await client.GetFromJsonAsync<ClaimPage>($"{ClaimsPath}?{filter}&page=3&pageSize=7", ResponseJson);

        Assert.NotNull(page);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(3, page.Page);
        Assert.Equal(7, page.PageSize);
    }

    [Theory]
    [InlineData("/api/claims?assignedHandlerName=", 256, "AssignedHandlerName")]
    [InlineData("/api/policies/search?q=", 256, "Q")]
    [InlineData("/api/reference/cause-of-loss-codes?perilCategory=", 51, "PerilCategory")]
    public async Task ReadsRejectOverlongFilters(string path, int length, string invalidField)
    {
        using var client = factory.CreateApiClient();
        await Authenticate(client, factory);
        using var response = await client.GetAsync(path + new string('x', length));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(invalidField, out _));
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
        Assert.Equal(response.Headers.GetValues("X-Correlation-ID").Single(),
            problem.RootElement.GetProperty("correlationId").GetString());
    }

    [Theory]
    [InlineData("/api/claims")]
    [InlineData("/api/claims/validate")]
    public async Task IntakeReturnsCorrelatedValidationProblemForAnInvalidDate(string path)
    {
        await using var api = new ClaimsApiFactory();
        using var client = api.CreateApiClient();
        await Authenticate(client, api);
        using var response = await client.PostAsJsonAsync(path, new { lossEvent = new { lossDate = "bad date" } });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ValidationError", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(422, problem.RootElement.GetProperty("status").GetInt32());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("$.lossEvent.lossDate", out _));
        Assert.Equal(response.Headers.GetValues("X-Correlation-ID").Single(),
            problem.RootElement.GetProperty("correlationId").GetString());
        Assert.Contains("X-Correlation-ID", response.Headers.GetValues("Access-Control-Expose-Headers"));
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

        var statuses = new[] { ClaimStatus.Draft, ClaimStatus.Open };
        var sql = context.Set<Claim>().Where(row => statuses.Contains(row.Status))
            .OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
            .Skip(20).Take(20)
            .Select(row => new { row.Status, row.LossEvent.LossDate, row.AssignedHandlerId })
            .ToQueryString();
        Assert.Contains("[OrganizationEntityId] =", sql);
        Assert.Contains(user.OrganizationEntityId.ToString("D"), sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[IsDeleted]", sql);
        Assert.Contains("FETCH NEXT", sql);
        Assert.Contains("[AssignedHandlerId]", sql);
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
                claimsConnection: database.ConnectionString, handlerDisplayName: "Alex Morgan");
            using var client = sqlApi.CreateApiClient();
            await Authenticate(client, sqlApi);
            var request = ValidRequest();
            var selectedPolicy = await VerifyReferenceData(client);

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

            var linkedRequest = request with { PolicyId = selectedPolicy.PolicyId };
            var concurrent = await Task.WhenAll(Post(client, linkedRequest, "second-claim"),
                Post(client, request, "third-claim"));
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

            var handlerId = sqlApi.Account(UserRoles.Handler).UserId;
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE dbo.Claims SET AssignedHandlerId = {handlerId}
                WHERE OrganizationEntityId = {SeededOrganization} AND Id = {second.Id}
                """);
            await VerifyClaimList(client, first, second, third, selectedPolicy.PolicyId, handlerId);

            await using var otherApi = new ClaimsApiFactory(demoEnabled: true, claimsConnection: database.ConnectionString);
            using var otherClient = otherApi.CreateApiClient();
            await Authenticate(otherClient, otherApi);
            using var hidden = await otherClient.GetAsync(firstRequest.Headers.Location);
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
            using var hiddenCoverage = await otherClient.GetAsync($"/api/policies/{selectedPolicy.PolicyId}/coverage");
            Assert.Equal(HttpStatusCode.NotFound, hiddenCoverage.StatusCode);
            Assert.Empty(await otherClient.GetFromJsonAsync<PolicySearchResult[]>("/api/policies/search?q=Meridian") ?? []);
            Assert.Empty(await otherClient.GetFromJsonAsync<CauseOfLossCode[]>("/api/reference/cause-of-loss-codes") ?? []);
            var hiddenList = await otherClient.GetFromJsonAsync<ClaimPage>(ClaimsPath, ResponseJson);
            Assert.NotNull(hiddenList);
            Assert.Equal(0, hiddenList.TotalCount);
            Assert.Empty(hiddenList.Items);
            var hiddenNameMatch = await otherClient.GetFromJsonAsync<ClaimPage>(
                $"{ClaimsPath}?assignedHandlerName=handler", ResponseJson);
            Assert.NotNull(hiddenNameMatch);
            Assert.Equal(0, hiddenNameMatch.TotalCount);
            Assert.Empty(hiddenNameMatch.Items);

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

    internal static CreateClaimRequest ValidRequest() => new(null, "Property",
        new LossDetails(new DateTimeOffset(2025, 6, 1, 10, 0, 0, TimeSpan.Zero), "COL-FIRE",
            "A fire damaged the insured warehouse."),
        [new PartyDetails(ClaimPartyRole.Claimant, ClaimPartyType.Person, "Alex", "Taylor")]);

    private static async Task<PolicySearchResult> VerifyReferenceData(HttpClient client)
    {
        var hits = await client.GetFromJsonAsync<PolicySearchResult[]>("/api/policies/search?q=Meridian");
        var policy = Assert.Single(Assert.IsType<PolicySearchResult[]>(hits));
        Assert.Equal("POL-2024-001001", policy.PolicyNumber);
        Assert.Contains("Vehicle", policy.CoverageTypes);

        var coverage = await client.GetFromJsonAsync<PolicyCoverage>($"/api/policies/{policy.PolicyId}/coverage");
        Assert.NotNull(coverage);
        Assert.Equal(policy.CoverageTypes, coverage.CoverageTypes);

        var causes = await client.GetFromJsonAsync<CauseOfLossCode[]>(
            "/api/reference/cause-of-loss-codes?perilCategory=Weather");
        Assert.NotNull(causes);
        Assert.Equal(new[] { "COL-FLOOD", "COL-WIND" }, causes.Select(cause => cause.Code));
        return policy;
    }

    private static async Task VerifyClaimList(HttpClient client, CreatedClaim first, CreatedClaim second,
        CreatedClaim third, Guid policyId, Guid handlerId)
    {
        const string filters = "status=Draft&status=Open&dateFrom=2025-06-01&dateTo=2025-06-01&causeOfLossCode=col-fire";
        var page1 = await client.GetFromJsonAsync<ClaimPage>($"{ClaimsPath}?{filters}&pageSize=2", ResponseJson);
        var page2 = await client.GetFromJsonAsync<ClaimPage>($"{ClaimsPath}?{filters}&page=2&pageSize=2", ResponseJson);
        Assert.NotNull(page1);
        Assert.NotNull(page2);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);
        var returnedIds = page1.Items.Concat(page2.Items).Select(row => row.Id).Order();
        Assert.Equal(new[] { first.Id, second.Id, third.Id }.Order(), returnedIds);

        var linked = await client.GetFromJsonAsync<ClaimPage>(
            $"{ClaimsPath}?policyId={policyId}&assignedHandlerId={handlerId}&search=Meridian", ResponseJson);
        Assert.NotNull(linked);
        var match = Assert.Single(linked.Items);
        Assert.Equal(second.Id, match.Id);
        Assert.Equal(handlerId, match.AssignedHandlerId);
        Assert.Equal("COL-FIRE", match.CauseOfLossCode);

        const string nameFilter = "assignedHandlerName=%20mORg%20";
        var byName = await client.GetFromJsonAsync<ClaimPage>($"{ClaimsPath}?{nameFilter}&pageSize=1", ResponseJson);
        Assert.NotNull(byName);
        Assert.Equal(1, byName.TotalCount);
        Assert.Equal(second.Id, Assert.Single(byName.Items).Id);

        var intersection = await client.GetFromJsonAsync<ClaimPage>(
            $"{ClaimsPath}?{nameFilter}&assignedHandlerId={handlerId}&page=2&pageSize=1", ResponseJson);
        Assert.NotNull(intersection);
        Assert.Equal(1, intersection.TotalCount);
        Assert.Empty(intersection.Items);
    }

    internal static async Task Authenticate(HttpClient client, ClaimsApiFactory host, string role = UserRoles.Handler)
    {
        using var login = await client.PostAsJsonAsync("/api/auth/demo-token",
            new { username = role, password = host.Account(role).Password });
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

    private sealed record TestCurrentUser(UserIdentity Identity) : ICurrentUser;
}
