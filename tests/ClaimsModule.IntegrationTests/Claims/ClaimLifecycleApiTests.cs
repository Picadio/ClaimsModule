using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Reference;
using ClaimsModule.Application.Reserves;
using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.IntegrationTests.Security;
using ClaimsModule.Persistence.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClaimsModule.IntegrationTests.Claims;

public sealed class ClaimLifecycleApiTests(ClaimsApiFactory factory) : IClassFixture<ClaimsApiFactory>
{
    private const string ClaimPath = "/api/claims/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string ReferencePath = "/api/reference/claim-statuses";
    private static readonly Guid SeededOrganization = Guid.Parse("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData(ClaimPath + "/status", "PUT")]
    [InlineData(ClaimPath + "/audit", "GET")]
    [InlineData(ClaimPath + "/closure-check", "GET")]
    [InlineData(ReferencePath, "GET")]
    public async Task LifecycleEndpointsRequireAuthentication(string path, string method)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(Guid.Empty, Correlation(response));
    }

    [Theory]
    [InlineData("{}", "TargetStatus")]
    [InlineData("""{"targetStatus":123}""", "TargetStatus")]
    [InlineData("""{"targetStatus":"Open","assignedHandlerId":"00000000-0000-0000-0000-000000000000"}""", "AssignedHandlerId")]
    public async Task RejectsInvalidStatusPayloadBeforeAccessingSql(string json, string field)
    {
        using var client = factory.CreateApiClient();
        await FnolApiTests.Authenticate(client, factory);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PutAsync(ClaimPath + "/status", content);
        await AssertValidation(response, field);
    }

    [Theory]
    [InlineData("page=0", "Page")]
    [InlineData("pageSize=101", "PageSize")]
    [InlineData("page=2147483647", "Page")]
    public async Task RejectsInvalidAuditPageBeforeAccessingSql(string query, string field)
    {
        using var client = factory.CreateApiClient();
        await FnolApiTests.Authenticate(client, factory);
        using var response = await client.GetAsync($"{ClaimPath}/audit?{query}");
        await AssertValidation(response, field);
    }

    [SqlServerFact]
    public async Task TransitionsAndAuditAreAtomicRoleGatedIdempotentAndTenantScoped()
    {
        var database = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(FnolApiTests.SqlServerVariable))
        {
            InitialCatalog = $"ClaimsModuleLifecycle_{Guid.NewGuid():N}"
        };
        var dbOptions = new DbContextOptionsBuilder<ClaimsDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new ClaimsDbContext(dbOptions);
        try
        {
            await db.Database.MigrateAsync();
            await using var api = new ClaimsApiFactory(demoEnabled: true, organizationId: SeededOrganization,
                claimsConnection: database.ConnectionString);
            using var handler = api.CreateApiClient();
            using var supervisor = api.CreateApiClient();
            await FnolApiTests.Authenticate(handler, api);
            await FnolApiTests.Authenticate(supervisor, api, UserRoles.Supervisor);
            await VerifyReference(handler, supervisor);

            var expiredPolicy = Guid.Parse("1a46cfff-f909-44dc-ad15-000000000005");
            var path = await CreateDraft(handler, FnolApiTests.ValidRequest() with { PolicyId = expiredPolicy });
            var invalidTransition = await RejectedStatus(handler, path, new(ClaimStatus.Closed), "TargetStatus");
            Assert.Contains("Available next statuses are Open.", invalidTransition.GetProperty("TargetStatus")[0].GetString());
            var draftCheck = await ReadClosureCheck(handler, path);
            Assert.False(draftCheck.TransitionAllowed);
            Assert.False(draftCheck.CanClose);
            Assert.Equal(invalidTransition.GetProperty("TargetStatus")[0].GetString(),
                Assert.Single(draftCheck.Blockers["TargetStatus"]));
            await RejectedStatus(handler, path, new(ClaimStatus.Open), "AssignedHandlerId");
            await RejectedStatus(handler, path, new(ClaimStatus.Open, AssignedHandlerId: Guid.NewGuid()),
                "AssignedHandlerId");
            var open = new ChangeClaimStatusRequest(ClaimStatus.Open, AssignedHandlerId: api.Account(UserRoles.Handler).UserId);
            await RejectedStatus(handler, path, open, "AcknowledgePolicyPeriodWarning");
            var draftAudit = await ReadAudit(handler, path);
            Assert.DoesNotContain(draftAudit.Items, item => item.EventType == "STATUS_CHANGED" || item.EventType == "HANDLER_ASSIGNED");

            var opened = await Change(handler, path, open with { AcknowledgePolicyPeriodWarning = true }, "open");
            Assert.Equal(ClaimStatus.Open, opened.Result.Status);
            Assert.Equal(open.AssignedHandlerId, opened.Result.AssignedHandlerId);
            var afterOpen = await ReadAudit(handler, path);
            Assert.Equal(new[] { "STATUS_CHANGED", "VALIDATION_WARNING_ACKNOWLEDGED", "HANDLER_ASSIGNED" },
                afterOpen.Items.Where(item => item.CorrelationId == opened.CorrelationId).Select(item => item.EventType));
            await RejectedStatus(handler, path, new(ClaimStatus.Closed), "TargetStatus");
            Assert.False((await ReadClosureCheck(handler, path)).TransitionAllowed);
            await RejectedStatus(handler, path, new(ClaimStatus.PendingPayment), "TargetStatus");
            await Change(handler, path, new(ClaimStatus.UnderInvestigation));
            await RejectedStatus(handler, path, new(ClaimStatus.Withdrawn), "Reason");
            await VerifyClosureBlockers(db, supervisor, path, opened.Result.Id);
            var closed = await Change(supervisor, path, new(ClaimStatus.Closed, "Investigation complete"));
            Assert.Equal("Investigation complete", closed.Result.ClosureReason);
            Assert.NotNull(closed.Result.ClosedAt);

            await RejectedStatus(handler, path, new(ClaimStatus.Reopened, "New evidence"), "TargetStatus");
            await RejectedStatus(supervisor, path, new(ClaimStatus.Reopened), "Reason");

            var reopen = new ChangeClaimStatusRequest(ClaimStatus.Reopened, "New evidence");
            var replies = await Task.WhenAll(Change(supervisor, path, reopen, "reopen"),
                Change(supervisor, path, reopen, "reopen"));
            Assert.Equal(ClaimStatus.Open, replies[0].Result.Status);
            Assert.Equal(replies[0].Result, replies[1].Result);
            Assert.Null(replies[0].Result.ClosedAt);
            Assert.Null(replies[0].Result.ClosureReason);
            using var conflict = await PutStatus(supervisor, path, reopen with { Reason = "Other evidence" }, "reopen");
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

            var page1 = await ReadAudit(handler, path, "?pageSize=2");
            var page2 = await ReadAudit(handler, path, "?page=2&pageSize=2");
            Assert.Equal(page1.TotalCount, page2.TotalCount);
            var audit = await ReadAudit(handler, path);
            Assert.Equal(audit.Items.Take(4).Select(item => item.Id),
                page1.Items.Concat(page2.Items).Select(item => item.Id));
            var reopenedEvents = audit.Items.Take(3).ToArray();
            Assert.Collection(reopenedEvents,
                item => AssertStatusEvent(item, "Reopened", "Open"),
                item =>
                {
                    Assert.Equal("CLAIM_REOPENED", item.EventType);
                    Assert.Equal("New evidence", item.NewValues?.GetProperty("Reason").GetString());
                },
                item => AssertStatusEvent(item, "Closed", "Reopened"));
            var originalCorrelation = Assert.Single(reopenedEvents.Select(item => item.CorrelationId).Distinct());
            Assert.Contains(originalCorrelation, replies.Select(reply => reply.CorrelationId));
            Assert.All(reopenedEvents, item => Assert.Equal(api.Account(UserRoles.Supervisor).UserId, item.ActorId));

            var detail = await handler.GetFromJsonAsync<ClaimDetail>(path, JsonOptions);
            Assert.NotNull(detail);
            Assert.Equal(ClaimStatus.Open, detail.Status);
            Assert.Equal(open.AssignedHandlerId, detail.AssignedHandlerId);
            Assert.Equal(audit.Items.Take(10).Select(item => item.Id), detail.RecentAuditEntries.Select(item => item.Id));

            await VerifyConcurrentAuditPages(handler, path);
            await VerifyAuditHiddenFromOtherTenant(path, database.ConnectionString);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.Claims SET IsDeleted = 1 WHERE Id = {replies[0].Result.Id}");
            using var deletedAudit = await handler.GetAsync(path + "/audit");
            Assert.Equal(HttpStatusCode.NotFound, deletedAudit.StatusCode);
            using var deletedCheck = await handler.GetAsync(path + "/closure-check");
            Assert.Equal(HttpStatusCode.NotFound, deletedCheck.StatusCode);
            using var deletedReplay = await PutStatus(supervisor, path, reopen, "reopen");
            Assert.Equal(HttpStatusCode.NotFound, deletedReplay.StatusCode);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [SqlServerFact]
    public async Task CanCloseAnOpenClaimWhenConfiguredAndKeepsActorKeysSeparate()
    {
        var database = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(FnolApiTests.SqlServerVariable))
        {
            InitialCatalog = $"ClaimsModuleDirectClose_{Guid.NewGuid():N}"
        };
        var dbOptions = new DbContextOptionsBuilder<ClaimsDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new ClaimsDbContext(dbOptions);
        try
        {
            await db.Database.MigrateAsync();
            await using var api = new ClaimsApiFactory(demoEnabled: true, organizationId: SeededOrganization,
                claimsConnection: database.ConnectionString, allowDirectClosure: true);
            using var handler = api.CreateApiClient();
            using var supervisor = api.CreateApiClient();
            await FnolApiTests.Authenticate(handler, api);
            await FnolApiTests.Authenticate(supervisor, api, UserRoles.Supervisor);
            var path = await CreateDraft(handler, FnolApiTests.ValidRequest() with
            {
                PolicyId = Guid.Parse("1a46cfff-f909-44dc-ad15-000000000001"),
                InitialReserve = new InitialReserveRequest(ReserveComponentType.Indemnity, 1_000m)
            });
            var opened = await Change(handler, path,
                new(ClaimStatus.Open, AssignedHandlerId: api.Account(UserRoles.Handler).UserId), "shared-key");
            await VerifyClaimLockModes(api.Services, opened.Result.Id);
            var check = await ReadClosureCheck(supervisor, path);
            Assert.True(check.TransitionAllowed);
            Assert.True(check.CanClose);
            Assert.True(check.RequiresPositiveReserveConfirmation);
            Assert.Empty(check.Blockers);
            await RejectedStatus(supervisor, path, new(ClaimStatus.Closed), "ConfirmPositiveReserveBalance");
            var closed = await Change(supervisor, path,
                new(ClaimStatus.Closed, "Positive balance acknowledged", ConfirmPositiveReserveBalance: true), "shared-key");
            Assert.Equal(ClaimStatus.Closed, closed.Result.Status);
            Assert.Contains((await ReadAudit(handler, path)).Items, item => item.EventType == "CLAIM_CLOSED" &&
                item.CorrelationId == closed.CorrelationId);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task VerifyClaimLockModes(IServiceProvider services, Guid claimId)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundTenant>().Initialize(SeededOrganization);
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IClaimUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
        var resource = $"ClaimsModule:Claim:{SeededOrganization:D}:{claimId:D}";

        Assert.Equal("Shared", await unitOfWork.ExecuteClaimReadAsync(claimId, ReadLockMode, CancellationToken.None));
        Assert.Equal("Exclusive", await unitOfWork.ExecuteClaimWriteAsync(claimId, ReadLockMode, CancellationToken.None));

        Task<string> ReadLockMode(CancellationToken token) => db.Database
            .SqlQuery<string>($"SELECT APPLOCK_MODE('public', {resource}, 'Transaction') AS [Value]")
            .SingleAsync(token);
    }

    private static async Task VerifyReference(HttpClient handler, HttpClient supervisor)
    {
        var handlerRules = await handler.GetFromJsonAsync<ClaimStatusReference[]>(ReferencePath, JsonOptions);
        var supervisorRules = await supervisor.GetFromJsonAsync<ClaimStatusReference[]>(ReferencePath, JsonOptions);
        Assert.NotNull(handlerRules);
        Assert.NotNull(supervisorRules);
        Assert.Equal(Enum.GetValues<ClaimStatus>(), handlerRules.Select(rule => rule.Status));
        Assert.Empty(handlerRules.Single(rule => rule.Status == ClaimStatus.Closed).AllowedTransitions);
        Assert.Equal(ClaimStatus.Reopened,
            Assert.Single(supervisorRules.Single(rule => rule.Status == ClaimStatus.Closed).AllowedTransitions));
        Assert.Empty(supervisorRules.Single(rule => rule.Status == ClaimStatus.Reopened).AllowedTransitions);
        Assert.DoesNotContain(ClaimStatus.Closed, handlerRules.Single(rule => rule.Status == ClaimStatus.Open).AllowedTransitions);
    }

    private static async Task VerifyAuditHiddenFromOtherTenant(string path, string connectionString)
    {
        await using var otherApi = new ClaimsApiFactory(demoEnabled: true, claimsConnection: connectionString);
        using var other = otherApi.CreateApiClient();
        await FnolApiTests.Authenticate(other, otherApi, UserRoles.Supervisor);
        using var read = await other.GetAsync(path + "/audit");
        using var check = await other.GetAsync(path + "/closure-check");
        using var write = await PutStatus(other, path, new(ClaimStatus.Reopened, "Hidden claim"), "reopen");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, check.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
    }

    private static async Task VerifyClosureBlockers(ClaimsDbContext db, HttpClient client, string path, Guid claimId)
    {
        await SetCriticalState(false);

        var blockers = await RejectedStatus(client, path, new(ClaimStatus.Closed), "Parties");
        Assert.True(blockers.TryGetProperty("LossEvent.CauseOfLossCode", out _));
        var auditBefore = await ReadAudit(client, path);
        var check = await ReadClosureCheck(client, path);
        Assert.True(check.TransitionAllowed);
        Assert.False(check.CanClose);
        Assert.False(check.RequiresPositiveReserveConfirmation);
        Assert.Equal(blockers.GetProperty("Parties")[0].GetString(), Assert.Single(check.Blockers["Parties"]));
        Assert.Equal(blockers.GetProperty("LossEvent.CauseOfLossCode")[0].GetString(),
            Assert.Single(check.Blockers["LossEvent.CauseOfLossCode"]));
        Assert.Equal(auditBefore.TotalCount, (await ReadAudit(client, path)).TotalCount);

        await SetCriticalState(true);
        Assert.True((await ReadClosureCheck(client, path)).CanClose);

        Task SetCriticalState(bool isActive) => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.ClaimParties SET IsActive = {isActive} WHERE ClaimId = {claimId};
            UPDATE dbo.CauseOfLossCodes SET IsActive = {isActive}
            WHERE OrganizationEntityId = {SeededOrganization} AND Code = 'COL-FIRE';
            """);
    }

    private static async Task<ClaimClosureCheck> ReadClosureCheck(HttpClient client, string path) =>
        Assert.IsType<ClaimClosureCheck>(await client.GetFromJsonAsync<ClaimClosureCheck>(path + "/closure-check", JsonOptions));

    private static async Task VerifyConcurrentAuditPages(HttpClient client, string path)
    {
        var writes = MoveToInvestigationAndBack();
        var reads = Enumerable.Range(0, 6).Select(async _ =>
        {
            var page = await ReadAudit(client, path, "?pageSize=100");
            Assert.Equal(page.TotalCount, page.Items.Count);
        });
        await Task.WhenAll(reads.Append(writes));

        async Task MoveToInvestigationAndBack()
        {
            await Change(client, path, new(ClaimStatus.UnderInvestigation));
            await Change(client, path, new(ClaimStatus.Open));
        }
    }

    private static async Task<string> CreateDraft(HttpClient client, CreateClaimRequest request)
    {
        using var response = await client.PostAsJsonAsync("/api/claims", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return response.Headers.Location.IsAbsoluteUri
            ? response.Headers.Location.AbsolutePath
            : response.Headers.Location.OriginalString;
    }

    private static async Task<(ClaimStatusResult Result, Guid CorrelationId)> Change(HttpClient client, string path,
        ChangeClaimStatusRequest change, string? key = null)
    {
        using var response = await PutStatus(client, path, change, key);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ClaimStatusResult>(JsonOptions);
        Assert.NotNull(result);
        return (result, Correlation(response));
    }

    private static async Task<JsonElement> RejectedStatus(HttpClient client, string path, ChangeClaimStatusRequest change,
        string field)
    {
        using var response = await PutStatus(client, path, change, key: null);
        return await AssertValidation(response, field);
    }

    private static async Task<HttpResponseMessage> PutStatus(HttpClient client, string path,
        ChangeClaimStatusRequest change, string? key)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, path + "/status")
        {
            Content = JsonContent.Create(change, options: JsonOptions)
        };
        if (key is not null)
        {
            message.Headers.Add("Idempotency-Key", key);
        }

        return await client.SendAsync(message);
    }

    private static async Task<ClaimAuditPage> ReadAudit(HttpClient client, string path, string query = "")
    {
        var page = await client.GetFromJsonAsync<ClaimAuditPage>(path + "/audit" + query);
        Assert.NotNull(page);
        return page;
    }

    private static async Task<JsonElement> AssertValidation(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out _));
        return errors.Clone();
    }

    private static Guid Correlation(HttpResponseMessage response) =>
        Guid.Parse(Assert.Single(response.Headers.GetValues("X-Correlation-ID")));

    private static void AssertStatusEvent(ClaimAuditItem item, string previous, string next)
    {
        Assert.Equal("STATUS_CHANGED", item.EventType);
        Assert.Equal(previous, item.OldValues?.GetProperty("Status").GetString());
        Assert.Equal(next, item.NewValues?.GetProperty("Status").GetString());
    }
}
