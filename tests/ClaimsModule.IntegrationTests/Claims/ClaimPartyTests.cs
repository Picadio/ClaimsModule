using System.Net.Http.Json;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Exceptions;
using ClaimsModule.IntegrationTests.Security;
using Xunit;

namespace ClaimsModule.IntegrationTests.Claims;

public sealed class ClaimPartyTests(ClaimsApiFactory factory) : IClassFixture<ClaimsApiFactory>
{
    [Fact]
    public void DeactivatesPartiesWithoutDeactivatingTheLastActiveClaimant()
    {
        var now = DateTimeOffset.UtcNow;
        var actor = Guid.NewGuid();
        var party = new PartyDetails(ClaimPartyRole.Claimant, ClaimPartyType.Person, "Ada", "Gray");
        var claim = Claim.Report(Guid.NewGuid(), actor, now, 1, new FnolDetails("Property",
            new LossDetails(now.AddDays(-1), "FIRE", "A qualifying property loss occurred."), [party], []), null, true);
        var first = Assert.Single(claim.Parties);
        Assert.Throws<ClaimRuleViolation>(() => claim.DeactivateParty(first, actor, now));

        var second = claim.AddParty(party with { FirstName = "Ben" }, actor, now.AddMinutes(1));
        Assert.True(claim.DeactivateParty(first, actor, now.AddMinutes(2)));
        Assert.False(claim.DeactivateParty(first, actor, now.AddMinutes(3)));
        Assert.Throws<ClaimRuleViolation>(() => claim.DeactivateParty(second, actor, now));
        Assert.Equal(2, claim.Parties.Count);
        Assert.False(first.IsActive);
        Assert.Equal(now.AddMinutes(2), claim.UpdatedAt);
    }

    [Fact]
    public async Task AddPartyValidatesBeforeReadingClaim()
    {
        using var client = factory.CreateApiClient();
        await FnolApiTests.Authenticate(client, factory);
        using var response = await client.PostAsJsonAsync($"/api/claims/{Guid.NewGuid():D}/parties",
            new PartyDetails((ClaimPartyRole)999, ClaimPartyType.Person));
        await ReserveApiTests.AssertError(response, "Role");
    }
}
