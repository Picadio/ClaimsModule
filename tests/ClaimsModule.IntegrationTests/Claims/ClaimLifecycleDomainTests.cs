using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Exceptions;
using Xunit;

namespace ClaimsModule.IntegrationTests.Claims;

public sealed class ClaimLifecycleDomainTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Handler = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ClaimTransitionDetails NoDetails = new(null, null, false, false, false);
    private static readonly ClaimLifecycleFacts NoReserves = new(true, default);

    [Fact]
    public void DraftRequiresAHandlerAndAnAcknowledgmentOfThePolicyPeriodWarning()
    {
        var claim = CreateDraft(new LinkedPolicy(Guid.NewGuid(), "POL-1", "Demo Insured",
            new DateOnly(2023, 1, 1), new DateOnly(2023, 12, 31)));

        AssertViolation("AssignedHandlerId", () => Change(claim, ClaimStatus.Open));
        var assign = NoDetails with { AssignedHandlerId = Handler };
        AssertViolation("AcknowledgePolicyPeriodWarning", () => Change(claim, ClaimStatus.Open, assign));
        Assert.Equal(ClaimStatus.Draft, claim.Status);
        Assert.Null(claim.AssignedHandlerId);
        Assert.Null(claim.UpdatedAt);

        var opened = Change(claim, ClaimStatus.Open, assign with { AcknowledgePolicyPeriodWarning = true });
        Assert.Equal(ClaimStatus.Open, claim.Status);
        Assert.Equal(Handler, claim.AssignedHandlerId);
        Assert.Equal(Actor, claim.UpdatedByUserId);
        Assert.Equal(Now.AddHours(1), claim.UpdatedAt);
        Assert.Equal("LOSS_OUTSIDE_POLICY_PERIOD", opened.AcknowledgedWarning?.Code);
    }

    [Fact]
    public void CannotOpenOrReopenWithCriticalLossData()
    {
        var claim = CreateDraft();
        var facts = NoReserves with { CauseOfLossIsActive = false };
        AssertViolation("LossEvent.CauseOfLossCode", () =>
            Change(claim, ClaimStatus.Open, NoDetails with { AssignedHandlerId = Handler }, facts));
        Assert.Equal(ClaimStatus.Draft, claim.Status);

        Change(claim, ClaimStatus.Open, NoDetails with { AssignedHandlerId = Handler });
        Change(claim, ClaimStatus.Closed);
        AssertViolation("LossEvent.CauseOfLossCode", () =>
            Change(claim, ClaimStatus.Reopened, NoDetails with { Reason = "New information" }, facts));
        Assert.Equal(ClaimStatus.Closed, claim.Status);
        Assert.NotNull(claim.ClosedAt);
    }

    [Fact]
    public void PendingPaymentRequiresApprovedReservesAndLiabilityFromInvestigation()
    {
        var claim = OpenClaim();
        Change(claim, ClaimStatus.UnderInvestigation);
        AssertViolation("TargetStatus", () =>
            Change(claim, ClaimStatus.PendingPayment, NoDetails with { LiabilityDetermined = true }));
        var approved = new ClaimLifecycleFacts(true, new ClaimReserveFacts(true, false, true));
        AssertViolation("LiabilityDetermined", () => Change(claim, ClaimStatus.PendingPayment, facts: approved));
        Assert.Equal(ClaimStatus.UnderInvestigation, claim.Status);

        var change = Change(claim, ClaimStatus.PendingPayment, NoDetails with { LiabilityDetermined = true }, approved);
        Assert.True(change.LiabilityDetermined);
        Assert.Equal(ClaimStatus.PendingPayment, claim.Status);
    }

    [Fact]
    public void ClosureBlocksPendingApprovalAndRequiresAnExplanationForPositiveBalances()
    {
        var claim = OpenClaim();
        var balance = new ClaimLifecycleFacts(true, new ClaimReserveFacts(true, false, true));
        var pending = balance with { Reserves = balance.Reserves with { HasPendingApproval = true } };
        var confirmed = NoDetails with { ConfirmPositiveReserveBalance = true, Reason = "  Remaining expense  " };
        AssertViolation("TargetStatus", () => Change(claim, ClaimStatus.Closed, confirmed, pending));
        AssertViolation("ConfirmPositiveReserveBalance", () => Change(claim, ClaimStatus.Closed, facts: balance));
        AssertViolation("ConfirmPositiveReserveBalance", () =>
            Change(claim, ClaimStatus.Closed, confirmed with { Reason = " " }, balance));
        Assert.Equal(ClaimStatus.Open, claim.Status);
        Assert.Null(claim.ClosedAt);

        var closed = Change(claim, ClaimStatus.Closed, confirmed, balance);
        Assert.True(closed.PositiveBalanceConfirmed);
        Assert.Equal("Remaining expense", claim.ClosureReason);
        Assert.Equal(Now.AddHours(1), claim.ClosedAt);
    }

    [Fact]
    public void ClosureReturnsAllBlockingConditionsWithoutChangingTheClaim()
    {
        var claim = OpenClaim();
        var blocked = new ClaimLifecycleFacts(false, new ClaimReserveFacts(true, true, true));

        var error = Assert.Throws<ClaimRuleViolation>(() => Change(claim, ClaimStatus.Closed, facts: blocked));

        Assert.Equal(new[] { "LossEvent.CauseOfLossCode", "TargetStatus", "ConfirmPositiveReserveBalance" },
            error.Errors.Select(issue => issue.Field));
        Assert.Equal(ClaimStatus.Open, claim.Status);
        Assert.Null(claim.ClosedAt);
    }

    [Fact]
    public void RequiresReasonsForWithdrawalAndReopeningAndClearsClosureMetadataOnReopen()
    {
        var claim = OpenClaim();
        AssertViolation("Reason", () => Change(claim, ClaimStatus.Withdrawn));
        Change(claim, ClaimStatus.Closed, NoDetails with { Reason = "Finalized" });
        AssertViolation("Reason", () => Change(claim, ClaimStatus.Reopened, NoDetails with { Reason = " " }));

        var change = Change(claim, ClaimStatus.Reopened, NoDetails with { Reason = "New information" });
        Assert.Equal(ClaimStatus.Closed, change.PreviousStatus);
        Assert.Equal(ClaimStatus.Reopened, change.TargetStatus);
        Assert.Equal(ClaimStatus.Open, change.FinalStatus);
        Assert.Equal(ClaimStatus.Open, claim.Status);
        Assert.Equal(Handler, claim.AssignedHandlerId);
        Assert.Null(claim.ClosedAt);
        Assert.Null(claim.ClosureReason);
    }

    private static Claim CreateDraft(LinkedPolicy? policy = null) => Claim.Report(Guid.NewGuid(), Actor, Now, 1,
        new FnolDetails("Property", new LossDetails(Now.AddDays(-1), "COL-FIRE", "Damage reported at the warehouse."),
            [new PartyDetails(ClaimPartyRole.Claimant, ClaimPartyType.Person, "Alex", "Taylor")], []), policy, true);

    private static Claim OpenClaim()
    {
        var claim = CreateDraft();
        Change(claim, ClaimStatus.Open, NoDetails with { AssignedHandlerId = Handler });
        return claim;
    }

    private static ClaimStatusChange Change(Claim claim, ClaimStatus target, ClaimTransitionDetails? details = null,
        ClaimLifecycleFacts? facts = null) => claim.ChangeStatus(target, details ?? NoDetails, facts ?? NoReserves,
        Actor, Now.AddHours(1));

    private static void AssertViolation(string field, Action action) =>
        Assert.Equal(field, Assert.Throws<ClaimRuleViolation>(action).Field);
}
