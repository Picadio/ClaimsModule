using System.Globalization;
using ClaimsModule.Domain.Exceptions;
using ClaimsModule.Domain.Reserves;
using Xunit;

namespace ClaimsModule.IntegrationTests.Claims;

public sealed class ReserveDomainTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("10000", ReserveComponentType.Indemnity, ReserveApprovalAuthority.Automatic)]
    [InlineData("10000.0001", ReserveComponentType.Indemnity, ReserveApprovalAuthority.Supervisor)]
    [InlineData("100000", ReserveComponentType.Expense, ReserveApprovalAuthority.Supervisor)]
    [InlineData("100000.0001", ReserveComponentType.ALAE, ReserveApprovalAuthority.Manager)]
    [InlineData("-10000", ReserveComponentType.SubrogationRecoverable, ReserveApprovalAuthority.Automatic)]
    [InlineData("-10000.0001", ReserveComponentType.SubrogationRecoverable, ReserveApprovalAuthority.Supervisor)]
    public void UsesAbsoluteDeltaForAuthorityAndOnlyAutoApprovalChangesBalance(string amountText,
        ReserveComponentType type, ReserveApprovalAuthority authority)
    {
        var amount = decimal.Parse(amountText, CultureInfo.InvariantCulture);
        var component = Create(type);

        var row = Submit(component, ReserveTransactionType.Add, amount);

        Assert.Equal(authority, row.RequiredAuthority);
        Assert.Equal(1, row.ChangeSequence);
        Assert.Equal(0m, row.PreviousBalance);
        Assert.Equal(amount, row.NewBalance);
        Assert.Equal(ReservePostingStatus.Pending, row.PostingStatus);
        var isAutomatic = authority == ReserveApprovalAuthority.Automatic;
        Assert.Equal(isAutomatic ? ReserveApprovalStatus.AutoApproved : ReserveApprovalStatus.PendingApproval, row.ApprovalStatus);
        Assert.Equal(isAutomatic ? amount : 0m, component.CurrentAmount);
        Assert.Equal(isAutomatic ? Now : (DateTimeOffset?)null, row.ApprovedAt);
    }

    [Fact]
    public void ChecksProjectedNetCapAndRetainsAProposalThatRequiresAnOverride()
    {
        var component = Create();
        var atLimit = Submit(component, ReserveTransactionType.Add, 10m, 9_999_990m);
        Assert.Equal(ReserveApprovalStatus.AutoApproved, atLimit.ApprovalStatus);

        var aboveLimit = Submit(component, ReserveTransactionType.Adjust, 0.0001m, 10_000_000m);
        Assert.True(aboveLimit.RequiresManagerOverride);
        Assert.Equal(ReserveApprovalAuthority.Automatic, aboveLimit.RequiredAuthority);
        Assert.Equal(ReserveApprovalStatus.PendingApproval, aboveLimit.ApprovalStatus);
        Assert.Null(aboveLimit.ApprovedAt);
        Assert.Equal(10.0001m, aboveLimit.NewBalance);

        var supervisoryChange = Submit(component, ReserveTransactionType.Adjust, 15_000m, 9_990_000m);
        Assert.True(supervisoryChange.RequiresManagerOverride);
        Assert.Equal(ReserveApprovalAuthority.Supervisor, supervisoryChange.RequiredAuthority);
        Assert.Equal(ReserveApprovalStatus.PendingApproval, supervisoryChange.ApprovalStatus);
        Assert.Equal(10m, component.CurrentAmount);
    }

    [Fact]
    public void CostsAndRecoveriesCannotCrossTheirBalanceSign()
    {
        var cost = Create();
        Assert.Throws<ClaimRuleViolation>(() => Submit(cost, ReserveTransactionType.Add, -1m));
        Submit(cost, ReserveTransactionType.Add, 10m);
        Assert.Throws<ClaimRuleViolation>(() => Submit(cost, ReserveTransactionType.Adjust, -10.0001m));

        var recovery = Create(ReserveComponentType.SubrogationRecoverable);
        Assert.Throws<ClaimRuleViolation>(() => Submit(recovery, ReserveTransactionType.Add, 1m));
        Submit(recovery, ReserveTransactionType.Add, -10m);
        Assert.Throws<ClaimRuleViolation>(() => Submit(recovery, ReserveTransactionType.Adjust, 10.0001m));
        var released = Submit(recovery, ReserveTransactionType.Adjust, 1.2345m);
        Assert.Equal(-8.7655m, released.NewBalance);
        Assert.Equal(released.NewBalance, recovery.CurrentAmount);
    }

    [Fact]
    public void KeepsOperationsImmutableAndReversesTheExactApprovedBalance()
    {
        var component = Create();
        var opening = Submit(component, ReserveTransactionType.Add, 300m);
        var adjustment = Submit(component, ReserveTransactionType.Adjust, -10m);
        Assert.Throws<ClaimRuleViolation>(() => Submit(component, ReserveTransactionType.Reverse, -300m));
        var reversal = Submit(component, ReserveTransactionType.Reverse, -290m);
        Assert.Equal(3, reversal.ChangeSequence);
        Assert.Equal(0m, component.CurrentAmount);
        Assert.Equal(300m, opening.NewBalance);
        Assert.Equal(290m, adjustment.NewBalance);
    }

    [Fact]
    public void RejectsAnUnrepresentableDeltaOrBalanceWithoutConsumingASequence()
    {
        var component = Create();
        Assert.Throws<ClaimRuleViolation>(() => Submit(component, ReserveTransactionType.Add, 1.00001m));
        Assert.Throws<ClaimRuleViolation>(() => Submit(component, ReserveTransactionType.Add, 1_000_000_000_000_000m));
        Submit(component, ReserveTransactionType.Add, 1m);

        Assert.Throws<ClaimRuleViolation>(() => Submit(component, ReserveTransactionType.Adjust,
            ClaimReserveComponent.MaximumAmount));
        Assert.Equal(1, component.LastChangeSequence);
        Assert.Equal(1m, component.CurrentAmount);
    }

    [Fact]
    public void PendingOpeningCannotBeBypassedWithAnotherAddOrAnAdjustment()
    {
        var component = Create();
        Submit(component, ReserveTransactionType.Add, 10_000.0001m);

        Assert.Throws<ClaimRuleViolation>(() => component.Submit(new(ReserveTransactionType.Add, 1m, "New opening"),
            0m, hasPendingAdd: true, Actor, Now));
        Assert.Throws<ClaimRuleViolation>(() => Submit(component, ReserveTransactionType.Adjust, 1m));
        Assert.Equal(0m, component.CurrentAmount);
        Assert.Equal(1, component.LastChangeSequence);
    }

    private static ClaimReserveComponent Create(ReserveComponentType type = ReserveComponentType.Indemnity) =>
        new(Guid.NewGuid(), Guid.NewGuid(), type, Actor, Now);

    private static ReserveHistory Submit(ClaimReserveComponent component, ReserveTransactionType type, decimal amount,
        decimal total = 0m) => component.Submit(new ReserveChange(type, amount, "Estimate updated"), total, false, Actor, Now);
}
