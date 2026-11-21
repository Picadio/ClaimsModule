using ClaimsModule.Domain.Exceptions;
using ClaimsModule.Domain.Reserves;
using Xunit;

namespace ClaimsModule.IntegrationTests.Claims;

public sealed class ReserveDecisionDomainTests
{
    private static readonly Guid Submitter = Guid.NewGuid();
    private static readonly Guid Reviewer = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(100_000, ReserveApprovalAuthority.Supervisor, true)]
    [InlineData(100_001, ReserveApprovalAuthority.Supervisor, false)]
    [InlineData(100_001, ReserveApprovalAuthority.Manager, true)]
    [InlineData(15_000, ReserveApprovalAuthority.Automatic, false)]
    public void ApprovalAndRejectionEnforceAmountAuthority(int amount, ReserveApprovalAuthority authority, bool allowed)
    {
        var component = Create();
        var entry = Submit(component, ReserveTransactionType.Add, amount);
        if (!allowed)
        {
            Assert.Equal("RequiredAuthority", Assert.Throws<ClaimRuleViolation>(() =>
                component.Approve(entry, authority, 0m, false, Reviewer, Now)).Field);
            Assert.Equal("RequiredAuthority", Assert.Throws<ClaimRuleViolation>(() =>
                entry.Reject(authority, Reviewer, Now, "Needs verification")).Field);
            Assert.Equal(ReserveApprovalStatus.PendingApproval, entry.ApprovalStatus);
            Assert.Equal(0m, component.CurrentAmount);
            return;
        }

        component.Approve(entry, authority, 0m, false, Reviewer, Now);
        Assert.Equal(ReserveApprovalStatus.Approved, entry.ApprovalStatus);
        Assert.Equal(Reviewer, entry.ApprovedByUserId);
        Assert.Equal(Now, entry.ApprovedAt);
        Assert.Equal(ReservePostingStatus.Pending, entry.PostingStatus);
        Assert.Equal((decimal)amount, component.CurrentAmount);
        Assert.Throws<ClaimRuleViolation>(() => component.Approve(entry, authority, 0m, false, Reviewer, Now));
    }

    [Fact]
    public void SubmitterCannotApproveTheirOwnTransaction()
    {
        var component = Create();
        var entry = Submit(component, ReserveTransactionType.Add, 20_000m);
        var error = Assert.Throws<ClaimRuleViolation>(() => component.Approve(entry, ReserveApprovalAuthority.Manager,
            0m, false, Submitter, Now));
        Assert.Equal("Self-approval is not permitted.", error.Message);
        Assert.Equal(ReserveApprovalStatus.PendingApproval, entry.ApprovalStatus);
    }

    [Fact]
    public void AppliesApprovedDeltaToCurrentBalanceAndKeepsSubmissionSnapshots()
    {
        var component = Create();
        Submit(component, ReserveTransactionType.Add, 7_000m);
        var pending = Submit(component, ReserveTransactionType.Adjust, 20_000m);
        Submit(component, ReserveTransactionType.Adjust, 500m);
        component.Approve(pending, ReserveApprovalAuthority.Supervisor, 7_500m, false, Reviewer, Now);

        Assert.Equal(27_500m, component.CurrentAmount);
        Assert.Equal(7_000m, pending.PreviousBalance);
        Assert.Equal(27_000m, pending.NewBalance);
        Assert.Equal(20_000m, pending.Amount);
    }

    [Fact]
    public void RejectsApprovalOfAStaleFullReversal()
    {
        var component = Create();
        var opening = Submit(component, ReserveTransactionType.Add, 20_000m);
        component.Approve(opening, ReserveApprovalAuthority.Supervisor, 0m, false, Reviewer, Now);
        var reversal = Submit(component, ReserveTransactionType.Reverse, -20_000m);
        Submit(component, ReserveTransactionType.Adjust, 500m);

        Assert.Equal("Amount", Assert.Throws<ClaimRuleViolation>(() => component.Approve(reversal,
            ReserveApprovalAuthority.Supervisor, 20_500m, false, Reviewer, Now)).Field);
        Assert.Equal(20_500m, component.CurrentAmount);
        Assert.Equal(ReserveApprovalStatus.PendingApproval, reversal.ApprovalStatus);
    }

    [Fact]
    public void RechecksTheClaimLimitAndUsesAnExistingOverrideAtSubmitAndApproval()
    {
        var component = Create();
        var entry = Submit(component, ReserveTransactionType.Add, 15_000m);
        Assert.False(entry.RequiresManagerOverride);
        Assert.Equal("Amount", Assert.Throws<ClaimRuleViolation>(() => component.Approve(entry,
            ReserveApprovalAuthority.Supervisor, 9_985_000.0001m, false, Reviewer, Now)).Field);

        component.Approve(entry, ReserveApprovalAuthority.Supervisor, 9_985_000.0001m, true, Reviewer, Now);
        var next = component.Submit(new(ReserveTransactionType.Adjust, 1m, "Minor correction"),
            10_000_000.0001m, false, Submitter, Now, hasLimitOverride: true);
        Assert.Equal(ReserveApprovalStatus.AutoApproved, next.ApprovalStatus);
        Assert.False(next.RequiresManagerOverride);
    }

    [Fact]
    public void RejectKeepsTheOriginalAndTheLinkedRevisionRequiresApproval()
    {
        var component = Create();
        var original = Submit(component, ReserveTransactionType.Add, 15_000m);
        Assert.Equal("RejectionReason", Assert.Throws<ClaimRuleViolation>(() =>
            original.Reject(ReserveApprovalAuthority.Supervisor, Reviewer, Now, " ")).Field);
        original.Reject(ReserveApprovalAuthority.Supervisor, Reviewer, Now, "  Verify estimate  ");
        var change = new ReserveChange(ReserveTransactionType.Add, 100m, "Revised estimate");
        Assert.Equal("ReplacesRejectedTransactionId", Assert.Throws<ClaimRuleViolation>(() => component.Submit(change,
            0m, false, Reviewer, Now, replaces: original)).Field);
        var revised = component.Submit(change, 0m, false, Submitter, Now, replaces: original);

        Assert.Equal(ReserveApprovalAuthority.Automatic, revised.RequiredAuthority);
        Assert.Equal(ReserveApprovalStatus.PendingApproval, revised.ApprovalStatus);
        Assert.Equal(original.Id, revised.ReplacesRejectedTransactionId);
        Assert.Equal(ReserveApprovalStatus.Rejected, original.ApprovalStatus);
        Assert.Equal(ReservePostingStatus.Cancelled, original.PostingStatus);
        Assert.Equal(15_000m, original.Amount);
        Assert.Equal("Verify estimate", original.RejectionReason);
        Assert.Equal(Reviewer, original.RejectedByUserId);
        Assert.Equal(0m, component.CurrentAmount);
    }

    [Fact]
    public void OnlyTheSubmitterCanRetractAPendingTransaction()
    {
        var component = Create();
        var pending = Submit(component, ReserveTransactionType.Add, 30_000m);
        Assert.Throws<ClaimRuleViolation>(() => pending.Retract(Reviewer, Now));
        pending.Retract(Submitter, Now);
        Assert.Equal(ReserveApprovalStatus.Cancelled, pending.ApprovalStatus);
        Assert.Equal(ReservePostingStatus.Cancelled, pending.PostingStatus);
        Assert.Equal(Submitter, pending.UpdatedByUserId);
        Assert.Equal(0m, component.CurrentAmount);
        Assert.Throws<ClaimRuleViolation>(() => pending.Retract(Submitter, Now));
    }

    private static ClaimReserveComponent Create() => new(Guid.NewGuid(), Guid.NewGuid(),
        ReserveComponentType.Indemnity, Submitter, Now);

    private static ReserveHistory Submit(ClaimReserveComponent component, ReserveTransactionType type, decimal amount) =>
        component.Submit(new(type, amount, "Updated estimate"), component.CurrentAmount, false, Submitter, Now);
}
