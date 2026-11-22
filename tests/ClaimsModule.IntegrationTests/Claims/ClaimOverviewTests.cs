using System.Net.Http.Json;
using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Exceptions;
using ClaimsModule.IntegrationTests.Security;
using Xunit;
using static ClaimsModule.IntegrationTests.Claims.ReserveApiTests;

namespace ClaimsModule.IntegrationTests.Claims;

public sealed class ClaimOverviewTests(ClaimsApiFactory factory) : IClassFixture<ClaimsApiFactory>
{
    [Fact]
    public void LinkingPolicyResolvesUnknownWarningAndUsesTheIntakeDateRule()
    {
        var now = DateTimeOffset.UtcNow;
        var actor = Guid.NewGuid();
        var claim = Claim.Report(Guid.NewGuid(), actor, now, 1, new FnolDetails("Property",
            new LossDetails(now.AddDays(-1), "FIRE", "A qualifying property loss occurred."),
            [new PartyDetails(ClaimPartyRole.Claimant, ClaimPartyType.Person)], [], ClaimSeverity.Critical), null, true);
        Assert.Equal(ClaimSeverity.Critical, claim.Severity);
        var expired = new LinkedPolicy(Guid.NewGuid(), "POL-EXPIRED", "Synthetic customer",
            new DateOnly(2000, 1, 1), new DateOnly(2001, 1, 1));
        var open = new ClaimTransitionDetails(null, Guid.NewGuid(), false, false, false);
        claim.ChangeStatus(ClaimStatus.Open, open, new(true, default), actor, now);

        Assert.Equal("AcknowledgePolicyPeriodWarning", Assert.Throws<ClaimRuleViolation>(() =>
            claim.LinkPolicy(expired, false, actor, now)).Field);
        Assert.Null(claim.PolicyId);
        var changed = claim.LinkPolicy(expired, true, actor, now);
        Assert.NotNull(changed);
        Assert.True(changed.Acknowledged);
        Assert.True(changed.ResolvedWarning?.IsDeleted);
        Assert.Equal("LOSS_OUTSIDE_POLICY_PERIOD", changed.AddedWarning?.Code);
        Assert.Null(claim.LinkPolicy(expired, false, actor, now));
        Assert.True(claim.SetNotes(" Review pending ", actor, now));
        Assert.Equal("Review pending", claim.Notes);
        Assert.False(claim.SetNotes("Review pending", actor, now.AddMinutes(1)));
    }

    [Fact]
    public async Task PreflightUsesTheIntakeValidationPipeline()
    {
        using var client = factory.CreateApiClient();
        await FnolApiTests.Authenticate(client, factory);
        var request = FnolApiTests.ValidRequest() with { Severity = (ClaimSeverity)999 };
        using var preflight = await client.PostAsJsonAsync("/api/claims/validate", request, JsonOptions);
        using var intake = await client.PostAsJsonAsync("/api/claims", request, JsonOptions);
        await AssertError(preflight, "Severity");
        await AssertError(intake, "Severity");
    }
}
