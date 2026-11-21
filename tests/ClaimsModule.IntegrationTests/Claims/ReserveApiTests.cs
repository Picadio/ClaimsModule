using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Reserves;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.IntegrationTests.Security;
using ClaimsModule.Persistence.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClaimsModule.IntegrationTests.Claims;

public sealed class ReserveApiTests(ClaimsApiFactory factory) : IClassFixture<ClaimsApiFactory>
{
    private const string ClaimsPath = "/api/claims";
    private const string ReservePath = ClaimsPath + "/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/reserves";
    private static readonly Guid SeededOrganization = Guid.Parse("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4");
    private static readonly Guid SeededPolicy = Guid.Parse("1a46cfff-f909-44dc-ad15-000000000001");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void InitialReserveDoesNotChangeExistingFnolFingerprintsWhenOmitted()
    {
        var request = FnolApiTests.ValidRequest();
        var oldShape = new { request.PolicyId, request.ClaimType, request.LossEvent, request.Parties, request.RiskObjects };

        Assert.Equal(JsonSerializer.Serialize(oldShape), JsonSerializer.Serialize(request));
        Assert.NotEqual(JsonSerializer.Serialize(oldShape),
            JsonSerializer.Serialize(request with { InitialReserve = new(ReserveComponentType.Indemnity, 100m) }));
    }

    [Theory]
    [InlineData("GET", ReservePath)]
    [InlineData("POST", ReservePath)]
    [InlineData("PUT", ReservePath + "/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")]
    public async Task ReserveRoutesRequireAuthentication(string method, string path)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"amount":1,"changeReason":"Valid reason"}""", "Component")]
    [InlineData("""{"component":"Expense","amount":1.00001,"changeReason":"Valid reason"}""", "Amount")]
    [InlineData("""{"component":"Expense","amount":1,"changeReason":" "}""", "ChangeReason")]
    public async Task InvalidReservePayloadIsRejectedBeforeAccessingSql(string json, string field)
    {
        using var client = factory.CreateApiClient();
        await FnolApiTests.Authenticate(client, factory);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(ReservePath, content);
        await AssertError(response, field);
    }

    [Fact]
    public async Task InvalidInitialReserveIsRejectedByTheFnolValidator()
    {
        using var client = factory.CreateApiClient();
        await FnolApiTests.Authenticate(client, factory);
        var invalid = FnolApiTests.ValidRequest() with { InitialReserve = new(ReserveComponentType.Indemnity, 0m) };
        using var response = await client.PostAsJsonAsync(ClaimsPath, invalid, JsonOptions);
        await AssertError(response, "InitialReserve.Amount");
    }

    [SqlServerFact]
    public async Task StoresReserveHistoryAtomicallyAndUsesTheApprovedBalanceInClaims()
    {
        var database = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(FnolApiTests.SqlServerVariable))
        {
            InitialCatalog = $"ClaimsModuleReserves_{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<ClaimsDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new ClaimsDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
            await using var api = new ClaimsApiFactory(demoEnabled: true, organizationId: SeededOrganization,
                claimsConnection: database.ConnectionString);
            using var client = api.CreateApiClient();
            await FnolApiTests.Authenticate(client, api);
            var request = FnolApiTests.ValidRequest() with { InitialReserve = new(ReserveComponentType.Indemnity, 1_000m) };
            using var unknownPolicy = await client.PostAsJsonAsync(ClaimsPath, request, JsonOptions);
            await AssertError(unknownPolicy, "PolicyId");
            request = request with { PolicyId = SeededPolicy };
            using var invalidAmount = await client.PostAsJsonAsync(ClaimsPath,
                request with { InitialReserve = new(ReserveComponentType.Indemnity, -1m) }, JsonOptions);
            await AssertError(invalidAmount, "Amount");

            using var first = await SendJson(client, HttpMethod.Post, ClaimsPath, request, "initial");
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            var draft = await Read<CreatedClaim>(first);
            Assert.EndsWith("-0000001", draft.ClaimNumber);
            Assert.NotNull(draft.InitialReserve);
            Assert.Equal(ReserveApprovalStatus.AutoApproved, draft.InitialReserve.ApprovalStatus);
            using var replayResponse = await SendJson(client, HttpMethod.Post, ClaimsPath, request, "initial");
            var replay = await Read<CreatedClaim>(replayResponse);
            Assert.Equal(draft.InitialReserve.Id, replay.InitialReserve?.Id);

            var path = $"{ClaimsPath}/{draft.Id:D}";
            var opened = new ChangeClaimStatusRequest(ClaimStatus.Open, AssignedHandlerId: api.Account("handler").UserId);
            using var openResponse = await client.PutAsJsonAsync(path + "/status", opened, JsonOptions);
            Assert.Equal(HttpStatusCode.OK, openResponse.StatusCode);
            using var pendingPayment = await client.PutAsJsonAsync(path + "/status",
                new ChangeClaimStatusRequest(ClaimStatus.PendingPayment), JsonOptions);
            Assert.Equal(HttpStatusCode.OK, pendingPayment.StatusCode);

            var reserves = path + "/reserves";
            var expense = new SubmitReserveRequest(ReserveComponentType.Expense, 2_000m, "Expected expense");
            var submissions = await Task.WhenAll(
                SendJson(client, HttpMethod.Post, reserves, expense, "expense"),
                SendJson(client, HttpMethod.Post, reserves, expense, "expense"));
            using var expenseResponse = submissions[0];
            using var repeatedExpense = submissions[1];
            Assert.Equal(HttpStatusCode.Created, expenseResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Created, repeatedExpense.StatusCode);
            var created = await Read<ReserveTransaction>(expenseResponse);
            Assert.Equal(created.Id, (await Read<ReserveTransaction>(repeatedExpense)).Id);
            Assert.Equal($"Reserve:{created.ReserveId:D}:Change:1", created.PostingKey);
            using var conflict = await SendJson(client, HttpMethod.Post, reserves, expense with { Amount = 2_001m }, "expense");
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

            var adjust = new AdjustReserveRequest(-500m, "Adjusted expense");
            var componentPath = $"{reserves}/{created.ReserveId:D}";
            using var adjusted = await SendJson(client, HttpMethod.Put, componentPath, adjust, "adjust");
            var transaction = await Read<ReserveTransaction>(adjusted);
            Assert.Equal(2, transaction.ChangeSequence);
            Assert.Equal(2_000m, transaction.PreviousBalance);
            Assert.Equal(1_500m, transaction.NewBalance);

            var large = new SubmitReserveRequest(ReserveComponentType.ALAE, 10_000_000m, "Large estimate");
            using var pendingResponse = await client.PostAsJsonAsync(reserves, large, JsonOptions);
            var pending = await Read<ReserveTransaction>(pendingResponse);
            Assert.Equal(ReserveApprovalStatus.PendingApproval, pending.ApprovalStatus);
            Assert.Equal("RESERVE_LIMIT_OVERRIDE_REQUIRED", Assert.Single(pending.Warnings).Code);
            using var competingAdd = await client.PostAsJsonAsync(reserves, large with { Amount = 1m }, JsonOptions);
            await AssertError(competingAdd, "TransactionType");
            using var blockedClose = await client.PutAsJsonAsync(path + "/status",
                new ChangeClaimStatusRequest(ClaimStatus.Closed), JsonOptions);
            await AssertError(blockedClose, "TargetStatus");

            var ledger = await client.GetFromJsonAsync<ClaimReserveLedger>(reserves, JsonOptions);
            Assert.NotNull(ledger);
            Assert.Equal(2_500m, ledger.TotalReserves);
            var expenseLedger = Assert.Single(ledger.Components, component => component.Component == ReserveComponentType.Expense);
            Assert.Equal(new[] { transaction.Id, created.Id }, expenseLedger.History.Select(row => row.Id));
            var pendingLedger = Assert.Single(ledger.Components, component => component.Component == ReserveComponentType.ALAE);
            Assert.Equal(0m, pendingLedger.CurrentAmount);
            Assert.Equal(10_000_000m, pendingLedger.PendingAmount);

            var detail = await client.GetFromJsonAsync<ClaimDetail>(path, JsonOptions);
            Assert.NotNull(detail);
            Assert.Equal(2_500m, detail.ReserveBalances.Sum(balance => balance.CurrentAmount));
            var list = await client.GetFromJsonAsync<ClaimPage>(ClaimsPath, JsonOptions);
            Assert.NotNull(list);
            Assert.Equal(2_500m, Assert.Single(list.Items).TotalReserves);
            var audit = await client.GetFromJsonAsync<ClaimAuditPage>(path + "/audit", JsonOptions);
            Assert.NotNull(audit);
            Assert.Single(audit.Items, row => row.EventType == "RESERVE_CREATED" && row.RelatedEntityId == created.Id);
            Assert.Single(audit.Items, row => row.EventType == "RESERVE_AUTO_APPROVED" && row.RelatedEntityId == created.Id);
            Assert.Single(audit.Items, row => row.EventType == "VALIDATION_ISSUE_ADDED" && row.RelatedEntityId == pending.Id);

            await AssertTenantIsolation(database.ConnectionString, reserves, componentPath, adjust);
            await AssertArchivedComponent(db, client, reserves, componentPath, created.ReserveId, expense, adjust);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task AssertTenantIsolation(string connectionString, string reserves, string componentPath,
        AdjustReserveRequest adjust)
    {
        await using var api = new ClaimsApiFactory(demoEnabled: true, claimsConnection: connectionString);
        using var client = api.CreateApiClient();
        await FnolApiTests.Authenticate(client, api);
        using var read = await client.GetAsync(reserves);
        using var write = await client.PutAsJsonAsync(componentPath, adjust, JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
    }

    private static async Task AssertArchivedComponent(ClaimsDbContext db, HttpClient client, string reserves,
        string componentPath, Guid reserveId, SubmitReserveRequest expense, AdjustReserveRequest adjust)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.ClaimReserveComponents SET IsDeleted = 1
            WHERE OrganizationEntityId = {SeededOrganization} AND Id = {reserveId}
            """);
        using var add = await client.PostAsJsonAsync(reserves, expense, JsonOptions);
        await AssertError(add, "Component");
        using var replay = await SendJson(client, HttpMethod.Post, reserves, expense, "expense");
        await AssertError(replay, "Component");
        using var change = await client.PutAsJsonAsync(componentPath, adjust, JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, change.StatusCode);
        var ledger = await client.GetFromJsonAsync<ClaimReserveLedger>(reserves, JsonOptions);
        Assert.NotNull(ledger);
        Assert.DoesNotContain(ledger.Components, component => component.Id == reserveId);
    }

    private static async Task<HttpResponseMessage> SendJson<T>(HttpClient client, HttpMethod method, string path,
        T payload, string idempotencyKey)
    {
        using var message = new HttpRequestMessage(method, path) { Content = JsonContent.Create(payload, options: JsonOptions) };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(message);
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        Assert.NotNull(value);
        return value;
    }

    private static async Task AssertError(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty(field, out _));
    }
}
