using System.Net;
using System.Net.Http.Json;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Reserves;
using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.IntegrationTests.Security;
using ClaimsModule.Persistence.Claims;
using MediatR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static ClaimsModule.IntegrationTests.Claims.ReserveApiTests;

namespace ClaimsModule.IntegrationTests.Claims;

public sealed class ReservePostingApiTests(ClaimsApiFactory factory) : IClassFixture<ClaimsApiFactory>
{
    private const string ClaimsPath = "/api/claims";
    private const string RetryPath =
        ClaimsPath + "/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/reserves/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb/retry-posting";
    private static readonly Guid Organization = Guid.Parse("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4");
    private static readonly Guid Policy = Guid.Parse("1a46cfff-f909-44dc-ad15-000000000001");

    [Fact]
    public async Task RetryRequiresAuthenticationAndValidIdempotencyKey()
    {
        using var client = factory.CreateApiClient();
        using var unauthorized = await client.PostAsync(RetryPath, null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        await FnolApiTests.Authenticate(client, factory);
        using var invalid = await SendJson(client, HttpMethod.Post, RetryPath, new { }, new string('x', 256));
        await AssertError(invalid, "IdempotencyKey");
    }

    [Fact]
    public void PostingSeekUsesSqlServerGuidOrdering()
    {
        using var db = new ClaimsDbContext(new DbContextOptionsBuilder<ClaimsDbContext>().UseSqlServer().Options);
        var cursor = Guid.NewGuid();
        var watermark = Guid.NewGuid();
        var sql = db.Set<ReserveHistory>().Where(entry => entry.Id.CompareTo(cursor) > 0 &&
                entry.Id.CompareTo(watermark) <= 0)
            .OrderBy(entry => entry.Id).Take(100).Select(entry => entry.Id).ToQueryString();
        Assert.Contains("WHERE", sql);
        Assert.Contains("[Id] >", sql);
        Assert.Contains("[Id] <=", sql);
        Assert.Contains("ORDER BY", sql);
    }

    [SqlServerFact]
    public async Task HangfirePostsInitialAutoApprovalAndManualApproval()
    {
        var connection = TestConnection();
        await using var db = CreateContext(connection);
        try
        {
            await db.Database.MigrateAsync();
            await using var api = new ClaimsApiFactory(demoEnabled: true, organizationId: Organization,
                claimsConnection: connection, glJobsEnabled: true);
            using var handler = api.CreateApiClient();
            using var supervisor = api.CreateApiClient();
            await FnolApiTests.Authenticate(handler, api);
            await FnolApiTests.Authenticate(supervisor, api, UserRoles.Supervisor);

            using var create = await handler.PostAsJsonAsync(ClaimsPath, FnolApiTests.ValidRequest() with
            {
                PolicyId = Policy,
                InitialReserve = new InitialReserveRequest(ReserveComponentType.Indemnity, 500m)
            }, JsonOptions);
            var claim = await Read<CreatedClaim>(create);
            Assert.NotNull(claim.InitialReserve);
            var path = $"{ClaimsPath}/{claim.Id:D}";
            var reserves = path + "/reserves";
            using var submit = await handler.PostAsJsonAsync(reserves,
                new SubmitReserveRequest(ReserveComponentType.Expense, 15_000m, "Expense estimate"), JsonOptions);
            var pending = await Read<ReserveTransaction>(submit);
            Assert.Equal(ReserveApprovalStatus.PendingApproval, pending.ApprovalStatus);
            Assert.Null((await GetLedger(handler, reserves)).Components.SelectMany(component => component.History)
                .Single(entry => entry.Id == pending.Id).PostingJobId);

            using var approve = await supervisor.PostAsync($"{reserves}/{pending.Id:D}/approve", null);
            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
            var ledger = await WaitForPosted(handler, reserves);
            Assert.Equal(15_500m, ledger.TotalReserves);
            foreach (var transaction in ledger.Components.SelectMany(component => component.History))
            {
                Assert.False(string.IsNullOrWhiteSpace(transaction.PostingJobId));
            }

            var audit = await handler.GetFromJsonAsync<ClaimAuditPage>(path + "/audit", JsonOptions);
            Assert.NotNull(audit);
            AssertPostingAudit(audit, claim.InitialReserve.Id, Correlation(create));
            AssertPostingAudit(audit, pending.Id, Correlation(approve));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [SqlServerFact]
    public async Task PostingRecoveryIsTenantScopedAndDeduplicatesAcrossFailureAndRetry()
    {
        var connection = TestConnection();
        await using var db = CreateContext(connection);
        try
        {
            await db.Database.MigrateAsync();
            await using var api = new ClaimsApiFactory(demoEnabled: true, organizationId: Organization, claimsConnection: connection);
            await using var testApi = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<IReservePostingQueue>(new TestPostingQueue())));
            using var handler = testApi.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
            await FnolApiTests.Authenticate(handler, api);
            using var create = await handler.PostAsJsonAsync(ClaimsPath, FnolApiTests.ValidRequest() with
            {
                PolicyId = Policy,
                InitialReserve = new InitialReserveRequest(ReserveComponentType.Indemnity, 500m)
            }, JsonOptions);
            var claim = await Read<CreatedClaim>(create);
            Assert.NotNull(claim.InitialReserve);
            var path = $"{ClaimsPath}/{claim.Id:D}";
            var reserves = path + "/reserves";
            using var decrease = await handler.PutAsJsonAsync($"{reserves}/{claim.InitialReserve.ReserveId:D}",
                new AdjustReserveRequest(-50m, "Reduced estimate"), JsonOptions);
            var approved = await Read<ReserveTransaction>(decrease);

            var services = testApi.Services;
            var first = Assert.Single(await Run(services, Organization, new ListPendingReservePostingsQuery(null, 1)));
            var second = Assert.Single(await Run(services, Organization,
                new ListPendingReservePostingsQuery(first.Target.TransactionId, 1)));
            Assert.NotEqual(first.Target.TransactionId, second.Target.TransactionId);
            Assert.Equal(second.Target.TransactionId, await Run(services, Organization, new FindPostingRecoveryWatermarkQuery()));
            var bounded = Assert.Single(await Run(services, Organization,
                new ListPendingReservePostingsQuery(null, 10, first.Target.TransactionId)));
            Assert.Equal(first.Target.TransactionId, bounded.Target.TransactionId);
            Assert.Empty(await Run(services, Guid.NewGuid(), new ListPendingReservePostingsQuery(null, 10)));

            var target = new ReservePostingTarget(Organization, claim.Id, approved.Id, approved.PostingKey);
            Assert.False(await Run(services, Guid.NewGuid(), new DispatchReservePostingCommand(target)));
            Assert.True(await Run(services, Organization, new DispatchReservePostingCommand(target)));
            Assert.False(await Run(services, Organization, new DispatchReservePostingCommand(target)));
            var assigned = await ReadTransaction(handler, reserves, approved.Id);
            Assert.NotNull(assigned.PostingJobId);
            var failure = new FinishReservePostingCommand(target, assigned.PostingJobId, "Terminal failure.");
            Assert.True(await Run(services, Organization, failure));
            Assert.False(await Run(services, Organization, failure));

            var retryPath = $"{reserves}/{approved.Id:D}/retry-posting";
            var beforeRetry = await ReadClaimUpdatedAt(db, claim.Id);
            using var retry = await SendJson(handler, HttpMethod.Post, retryPath, new { }, "retry");
            var requeued = await Read<ReserveTransaction>(retry);
            Assert.Equal(approved.PostingKey, requeued.PostingKey);
            Assert.Equal(ReservePostingStatus.Pending, requeued.PostingStatus);
            Assert.True(await Run(services, Organization, new DispatchReservePostingCommand(target)));
            var next = await ReadTransaction(handler, reserves, approved.Id);
            Assert.NotEqual(assigned.PostingJobId, next.PostingJobId);
            Assert.NotNull(next.PostingJobId);
            Assert.False(await Run(services, Organization, new FinishReservePostingCommand(target, assigned.PostingJobId)));

            var complete = new FinishReservePostingCommand(target, next.PostingJobId);
            var results = await Task.WhenAll(Run(services, Organization, complete), Run(services, Organization, complete));
            Assert.Single(results, completed => completed);
            using var retryReplay = await SendJson(handler, HttpMethod.Post, retryPath, new { }, "retry");
            Assert.Equal(requeued, await Read<ReserveTransaction>(retryReplay));

            var ledger = await GetLedger(handler, reserves);
            Assert.Equal(450m, ledger.TotalReserves);
            Assert.Equal(beforeRetry, await ReadClaimUpdatedAt(db, claim.Id));
            Assert.Equal(ReservePostingStatus.Posted, ledger.Components.SelectMany(component => component.History)
                .Single(entry => entry.Id == approved.Id).PostingStatus);
            var audit = await handler.GetFromJsonAsync<ClaimAuditPage>(path + "/audit", JsonOptions);
            Assert.NotNull(audit);
            var journal = AssertPostingAudit(audit, approved.Id, Correlation(retry));
            Assert.Equal("Outstanding Loss Reserves", journal.NewValues?.GetProperty("DebitAccount").GetString());
            Assert.Equal(50m, journal.NewValues?.GetProperty("Amount").GetDecimal());
            var failed = Assert.Single(audit.Items, item => item.EventType == "GL_POSTING_FAILED");
            Assert.Equal(Correlation(decrease), failed.CorrelationId);
            Assert.Single(audit.Items, item => item.EventType == "GL_POSTING_RETRY_REQUESTED");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static string TestConnection() => new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable(FnolApiTests.SqlServerVariable))
    {
        InitialCatalog = $"ClaimsModuleGL_{Guid.NewGuid():N}"
    }.ConnectionString;

    private static ClaimsDbContext CreateContext(string connection) =>
        new(new DbContextOptionsBuilder<ClaimsDbContext>().UseSqlServer(connection).Options);

    private static Task<DateTimeOffset?> ReadClaimUpdatedAt(ClaimsDbContext db, Guid id) =>
        db.Database.SqlQuery<DateTimeOffset?>(
            $"SELECT UpdatedAt AS [Value] FROM dbo.Claims WHERE OrganizationEntityId = {Organization} AND Id = {id}")
            .SingleAsync();

    private static async Task<T> Run<T>(IServiceProvider services, Guid organizationId, IRequest<T> request)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundTenant>().Initialize(organizationId);
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    private static async Task<ClaimReserveLedger> GetLedger(HttpClient client, string reserves) =>
        Assert.IsType<ClaimReserveLedger>(await client.GetFromJsonAsync<ClaimReserveLedger>(reserves, JsonOptions));

    private static async Task<ClaimReserveLedger> WaitForPosted(HttpClient client, string reserves)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        while (!timeout.IsCancellationRequested)
        {
            var ledger = await GetLedger(client, reserves);
            if (ledger.Components.SelectMany(component => component.History)
                .All(entry => entry.PostingStatus == ReservePostingStatus.Posted))
            {
                return ledger;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
        }

        throw new TimeoutException("The scheduled reserve postings did not finish.");
    }

    private static async Task<ReserveTransaction> ReadTransaction(HttpClient client, string reserves, Guid id) =>
        (await GetLedger(client, reserves)).Components.SelectMany(component => component.History).Single(entry => entry.Id == id);

    private static ClaimAuditItem AssertPostingAudit(ClaimAuditPage page, Guid transactionId, Guid correlation)
    {
        var entry = Assert.Single(page.Items, item => item.EventType == "GL_POSTING_SIMULATED" && item.RelatedEntityId == transactionId);
        Assert.Equal(correlation, entry.CorrelationId);
        Assert.Null(entry.ActorId);
        return entry;
    }

    private static Guid Correlation(HttpResponseMessage response) =>
        Guid.Parse(Assert.Single(response.Headers.GetValues("X-Correlation-ID")));

    private sealed class TestPostingQueue : IReservePostingQueue
    {
        public string Enqueue(ReservePostingTarget target) => Guid.NewGuid().ToString("D");
    }
}
