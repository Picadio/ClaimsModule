using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Exceptions;
using Xunit;

namespace ClaimsModule.IntegrationTests.Claims;

public sealed class FnolDomainTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly DateTimeOffset ReportedAt = new(2026, 1, 1, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReportsDraftWithClaimantAndEvent()
    {
        var claim = Report(Details());

        Assert.Equal(ClaimStatus.Draft, claim.Status);
        Assert.Equal("CLM-2026-0000001", claim.ClaimNumber);
        Assert.Equal(OrganizationId, claim.OrganizationEntityId);
        Assert.Equal(ActorId, claim.CreatedByUserId);
        Assert.Equal(ReportedAt, claim.CreatedEvent?.OccurredAt);
        Assert.Equal(ClaimPartyRole.Claimant, Assert.Single(claim.Parties).Role);
        Assert.Single(claim.RiskObjects);
        Assert.Empty(claim.Warnings);
    }

    [Theory]
    [InlineData("future", "LossEvent.LossDate")]
    [InlineData("short", "LossEvent.LossDescription")]
    [InlineData("inactive-cause", "LossEvent.CauseOfLossCode")]
    [InlineData("no-claimant", "Parties")]
    public void RejectsInvalidIntake(string problem, string field)
    {
        var details = Details();
        details = problem switch
        {
            "future" => details with { LossEvent = details.LossEvent with { LossDate = ReportedAt.AddSeconds(1) } },
            "short" => details with { LossEvent = details.LossEvent with { LossDescription = new string('a', 19) } },
            "no-claimant" => details with { Parties = [new PartyDetails(ClaimPartyRole.Witness, ClaimPartyType.Person)] },
            _ => details
        };

        var error = Assert.Throws<ClaimRuleViolation>(() => Report(details, problem != "inactive-cause"));

        Assert.Equal(field, error.Field);
    }

    [Fact]
    public void AllowsUnknownPolicyAndMissingRisksWithWarnings()
    {
        var claim = Claim.Report(OrganizationId, ActorId, ReportedAt, 1, Details() with { RiskObjects = [] },
            policy: null, causeOfLossIsActive: true);

        Assert.Equal(ClaimStatus.Draft, claim.Status);
        Assert.Null(claim.PolicyId);
        Assert.Collection(claim.Warnings,
            warning => Assert.Equal("POLICY_UNKNOWN", warning.Code),
            warning => Assert.Equal("RISK_OBJECTS_MISSING", warning.Code));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void PolicyEndDateIsInclusive(int daysAfterExpiry, bool withinPolicy)
    {
        var details = Details();
        var lastDay = ReportedAt.AddDays(-10);
        var policy = new LinkedPolicy(Guid.NewGuid(), "POL-1", "Demo Insured",
            DateOnly.FromDateTime(lastDay.AddYears(-1).UtcDateTime), DateOnly.FromDateTime(lastDay.UtcDateTime));
        var claim = Claim.Report(OrganizationId, ActorId, ReportedAt, 1,
            details with { LossEvent = details.LossEvent with { LossDate = lastDay.AddDays(daysAfterExpiry) } },
            policy, causeOfLossIsActive: true);

        Assert.Equal(policy.Id, claim.PolicyId);
        if (withinPolicy)
        {
            Assert.Empty(claim.Warnings);
        }
        else
        {
            Assert.Equal("LOSS_OUTSIDE_POLICY_PERIOD", Assert.Single(claim.Warnings).Code);
        }
    }

    [Fact]
    public void FormatsNumberUsingUtcYearAndRejectsOverflow()
    {
        var previousLocalYear = ReportedAt.ToOffset(TimeSpan.FromHours(-5));

        Assert.Equal("CLM-2026-9999999", FnolRules.FormatClaimNumber(previousLocalYear, 9_999_999));
        Assert.Throws<ArgumentOutOfRangeException>(() => FnolRules.FormatClaimNumber(previousLocalYear, 10_000_000));
    }

    private static Claim Report(FnolDetails details, bool activeCause = true)
    {
        var lossDay = DateOnly.FromDateTime(details.LossEvent.LossDate.UtcDateTime);
        var policy = new LinkedPolicy(Guid.NewGuid(), "POL-1", "Demo Insured", lossDay.AddDays(-1), lossDay);
        return Claim.Report(OrganizationId, ActorId, ReportedAt, 1, details, policy, activeCause);
    }

    private static FnolDetails Details() => new("Property",
        new LossDetails(ReportedAt.AddDays(-1), "COL-FIRE", "Fire damaged a warehouse loading dock."),
        [new PartyDetails(ClaimPartyRole.Claimant, ClaimPartyType.Person, "Alex", "Taylor")],
        [new RiskDetails(RiskAssetType.Property, "Warehouse", IsPrimary: true)]);
}
