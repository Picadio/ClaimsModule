using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Reserves;
using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.IntegrationTests.Security;
using ClaimsModule.Persistence.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static ClaimsModule.IntegrationTests.Claims.ReserveApiTests;

namespace ClaimsModule.IntegrationTests.Claims;

public sealed class ReserveDecisionApiTests(ClaimsApiFactory factory) : IClassFixture<ClaimsApiFactory>
{
    private const string ClaimsPath = "/api/claims";
    private const string ReservePath = ClaimsPath + "/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/reserves";
    private const string TransactionPath = ReservePath + "/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private static readonly Guid SeededOrganization = Guid.Parse("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4");
    private static readonly Guid SeededPolicy = Guid.Parse("1a46cfff-f909-44dc-ad15-000000000001");

    [Theory]
    [InlineData(TransactionPath + "/approve", "POST")]
    [InlineData(TransactionPath + "/reject", "POST")]
    [InlineData(ReservePath + "/manager-override", "PUT")]
    public async Task HandlerCannotReviewOrEnableAnOverride(string path, string method)
    {
        using var client = factory.CreateApiClient();
        await FnolApiTests.Authenticate(client, factory);
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Forbidden", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task DecisionReasonsAreRequiredBeforeAccessingSql()
    {
        using var supervisor = factory.CreateApiClient();
        using var manager = factory.CreateApiClient();
        await FnolApiTests.Authenticate(supervisor, factory, UserRoles.Supervisor);
        await FnolApiTests.Authenticate(manager, factory, UserRoles.Manager);
        using var reject = await supervisor.PostAsJsonAsync(TransactionPath + "/reject", new RejectReserveRequest(" "));
        await AssertError(reject, "RejectionReason");
        using var enable = await manager.PutAsJsonAsync(ReservePath + "/manager-override", new EnableReserveOverrideRequest(null));
        await AssertError(enable, "Reason");
    }

    [Fact]
    public void OmittedRejectedTransactionDoesNotChangeExistingSubmissionFingerprints()
    {
        var request = new SubmitReserveRequest(ReserveComponentType.Expense, 1_000m, "Updated estimate");
        var previous = new { request.Component, request.Amount, request.ChangeReason, request.TransactionType };
        var reserveId = Guid.NewGuid();
        Assert.Equal(JsonSerializer.Serialize(new { ReserveId = reserveId, request = previous }),
            JsonSerializer.Serialize(new { ReserveId = reserveId, request }));
        Assert.NotEqual(JsonSerializer.Serialize(request),
            JsonSerializer.Serialize(request with { ReplacesRejectedTransactionId = Guid.NewGuid() }));
    }

    [SqlServerFact]
    public async Task SerializesCrossComponentApprovalsAndStoresEveryDecisionWithAuditAndReplay()
    {
        var database = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(FnolApiTests.SqlServerVariable))
        {
            InitialCatalog = $"ClaimsModuleReserveDecisions_{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<ClaimsDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new ClaimsDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
            await using var api = new ClaimsApiFactory(demoEnabled: true, organizationId: SeededOrganization,
                claimsConnection: database.ConnectionString);
            using var handler = api.CreateApiClient();
            using var supervisor = api.CreateApiClient();
            using var manager = api.CreateApiClient();
            await FnolApiTests.Authenticate(handler, api);
            await FnolApiTests.Authenticate(supervisor, api, UserRoles.Supervisor);
            await FnolApiTests.Authenticate(manager, api, UserRoles.Manager);
            using var created = await handler.PostAsJsonAsync(ClaimsPath,
                FnolApiTests.ValidRequest() with { PolicyId = SeededPolicy }, JsonOptions);
            var claim = await Read<CreatedClaim>(created);
            var path = $"{ClaimsPath}/{claim.Id:D}";
            var reserves = path + "/reserves";
            var initial = await Submit(handler, reserves, new(ReserveComponentType.Indemnity, 9_800_000m, "Estimate"));
            using var denied = await Decide(supervisor, reserves, initial, "approve", "denied");
            await AssertError(denied, "RequiredAuthority");
            using var approved = await Decide(manager, reserves, initial, "approve", "approve-initial");
            var approval = await Read<ReserveTransaction>(approved);
            Assert.Equal(api.Account(UserRoles.Manager).UserId, approval.ApprovedByUserId);
            using var replay = await Decide(manager, reserves, initial, "approve", "approve-initial");
            Assert.Equal(approval, await Read<ReserveTransaction>(replay));

            var expense = await Submit(handler, reserves, new(ReserveComponentType.Expense, 150_000m, "Expense estimate"));
            var alae = await Submit(handler, reserves, new(ReserveComponentType.ALAE, 150_000m, "ALAE estimate"));
            var responses = await Task.WhenAll(Decide(manager, reserves, expense, "approve", "expense"),
                Decide(manager, reserves, alae, "approve", "alae"));
            using var expenseDecision = responses[0];
            using var alaeDecision = responses[1];
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var failedIndex = Array.FindIndex(responses, response => response.StatusCode != HttpStatusCode.OK);
            await AssertError(responses[failedIndex], "Amount");

            var overridePath = reserves + "/manager-override";
            using var scopeConflict = await SendJson(manager, HttpMethod.Put, overridePath,
                new EnableReserveOverrideRequest("Business justification"), "approve-initial");
            Assert.Equal(HttpStatusCode.Conflict, scopeConflict.StatusCode);
            var enable = new EnableReserveOverrideRequest("  Business justification  ");
            using var enabled = await SendJson(manager, HttpMethod.Put, overridePath, enable, "override");
            Assert.True((await Read<ReserveOverrideResult>(enabled)).ReserveLimitOverrideEnabled);
            using var enabledReplay = await SendJson(manager, HttpMethod.Put, overridePath, enable, "override");
            Assert.True((await Read<ReserveOverrideResult>(enabledReplay)).ReserveLimitOverrideEnabled);

            var blocked = failedIndex == 0 ? expense : alae;
            using var approvedAfterOverride = await Decide(manager, reserves, blocked, "approve",
                failedIndex == 0 ? "expense" : "alae");
            Assert.Equal(ReserveApprovalStatus.Approved, (await Read<ReserveTransaction>(approvedAfterOverride)).ApprovalStatus);
            await VerifyRejectedRevisionAndRetraction(supervisor, manager, reserves);

            var indemnityPath = $"{reserves}/{initial.ReserveId:D}";
            using var increase = await handler.PutAsJsonAsync(indemnityPath, new AdjustReserveRequest(11_000m, "Proposed increase"), JsonOptions);
            using var decrease = await handler.PutAsJsonAsync(indemnityPath, new AdjustReserveRequest(-11_000m, "Proposed decrease"), JsonOptions);
            Assert.Equal(ReserveApprovalStatus.PendingApproval, (await Read<ReserveTransaction>(increase)).ApprovalStatus);
            Assert.Equal(ReserveApprovalStatus.PendingApproval, (await Read<ReserveTransaction>(decrease)).ApprovalStatus);

            var ledger = await handler.GetFromJsonAsync<ClaimReserveLedger>(reserves, JsonOptions);
            Assert.NotNull(ledger);
            Assert.True(ledger.ReserveLimitOverrideEnabled);
            Assert.Equal(10_100_000m, ledger.TotalReserves);
            var indemnity = Assert.Single(ledger.Components, component => component.Id == initial.ReserveId);
            Assert.Equal(0m, indemnity.PendingAmount);
            Assert.Equal(ReserveApprovalStatus.PendingApproval, indemnity.ApprovalStatus);
            Assert.True(indemnity.HasApprovedTransaction);
            var approvedEntries = ledger.Components.SelectMany(component => component.History)
                .Where(entry => entry.ApprovedByUserId == api.Account(UserRoles.Manager).UserId).ToArray();
            Assert.NotEmpty(approvedEntries);
            Assert.All(approvedEntries, entry => Assert.Equal(UserRoles.Manager, entry.ApprovedByDisplayName));
            var audit = await handler.GetFromJsonAsync<ClaimAuditPage>(path + "/audit?pageSize=100", JsonOptions);
            Assert.NotNull(audit);
            Assert.Equal(3, audit.Items.Count(item => item.EventType == "RESERVE_APPROVED"));
            var overrideAudit = Assert.Single(audit.Items, item => item.EventType == "RESERVE_LIMIT_OVERRIDE_ENABLED");
            Assert.Equal("Business justification", overrideAudit.NewValues?.GetProperty("Reason").GetString());
            Assert.Equal(2, audit.Items.Count(item => item.EventType == "RESERVE_REJECTED"));
            var rejectionAudit = Assert.Single(audit.Items, item => item.EventType == "RESERVE_REJECTED" &&
                item.OldValues?.GetProperty("RejectionReason").GetString() == "Recheck cost");
            Assert.Equal("Recheck cost", rejectionAudit.OldValues?.GetProperty("RejectionReason").GetString());
            Assert.Single(audit.Items, item => item.EventType == "RESERVE_RETRACTED");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task VerifyRejectedRevisionAndRetraction(HttpClient submitter, HttpClient reviewer, string path)
    {
        var change = new SubmitReserveRequest(ReserveComponentType.Expense, 15_000m, "Updated estimate",
            ReserveTransactionType.Adjust);
        var transaction = await Submit(submitter, path, change);
        using var self = await Decide(submitter, path, transaction, "approve", "self");
        await AssertError(self, "ApprovalStatus");
        using var rejected = await SendJson(reviewer, HttpMethod.Post, $"{path}/{transaction.Id:D}/reject",
            new RejectReserveRequest("Recheck cost"), "reject");
        var rejection = await Read<ReserveTransaction>(rejected);
        Assert.Equal(ReserveApprovalStatus.Rejected, rejection.ApprovalStatus);
        Assert.Equal(ReservePostingStatus.Cancelled, rejection.PostingStatus);

        var firstRevision = change with { Amount = 100m, ReplacesRejectedTransactionId = transaction.Id };
        var secondRevision = firstRevision with { Amount = 200m };
        var responses = await Task.WhenAll(
            SendJson(submitter, HttpMethod.Post, path, firstRevision, "revision-first"),
            SendJson(submitter, HttpMethod.Post, path, secondRevision, "revision-second"));
        using var first = responses[0];
        using var second = responses[1];
        var winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        await AssertError(Assert.Single(responses, response => response != winner), "ReplacesRejectedTransactionId");
        var revised = await Read<ReserveTransaction>(winner);
        Assert.Equal(ReserveApprovalStatus.PendingApproval, revised.ApprovalStatus);
        Assert.Equal(transaction.Id, revised.ReplacesRejectedTransactionId);
        using var replay = await SendJson(submitter, HttpMethod.Post, path,
            winner == first ? firstRevision : secondRevision, winner == first ? "revision-first" : "revision-second");
        Assert.Equal(revised.Id, (await Read<ReserveTransaction>(replay)).Id);
        using var otherRetraction = await Decide(reviewer, path, revised, "retract", "not-owner");
        await AssertError(otherRetraction, "ApprovalStatus");
        using var retracted = await Decide(submitter, path, revised, "retract", "retract");
        var cancellation = await Read<ReserveTransaction>(retracted);
        Assert.Equal(ReserveApprovalStatus.Cancelled, cancellation.ApprovalStatus);
        Assert.Equal(ReservePostingStatus.Cancelled, cancellation.PostingStatus);
        Assert.Equal(revised.SubmittedByUserId, cancellation.RetractedByUserId);

        var nextRevision = await Submit(submitter, path, firstRevision);
        using var nextRejection = await SendJson(reviewer, HttpMethod.Post, $"{path}/{nextRevision.Id:D}/reject",
            new RejectReserveRequest("Still needs verification"), "reject-revision");
        Assert.Equal(ReserveApprovalStatus.Rejected, (await Read<ReserveTransaction>(nextRejection)).ApprovalStatus);
        using var ancestor = await submitter.PostAsJsonAsync(path, firstRevision, JsonOptions);
        await AssertError(ancestor, "ReplacesRejectedTransactionId");
        var latest = await Submit(submitter, path, firstRevision with { ReplacesRejectedTransactionId = nextRevision.Id });
        Assert.Equal(nextRevision.Id, latest.ReplacesRejectedTransactionId);
        Assert.Equal(ReserveApprovalStatus.PendingApproval, latest.ApprovalStatus);
    }

    private static async Task<ReserveTransaction> Submit(HttpClient client, string path, SubmitReserveRequest request)
    {
        using var response = await client.PostAsJsonAsync(path, request, JsonOptions);
        return await Read<ReserveTransaction>(response);
    }

    private static Task<HttpResponseMessage> Decide(HttpClient client, string path, ReserveTransaction transaction,
        string action, string key) => SendJson(client, HttpMethod.Post, $"{path}/{transaction.Id:D}/{action}", new { }, key);
}
