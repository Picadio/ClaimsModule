using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Infrastructure.Jobs;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClaimsModule.IntegrationTests.Claims;

public sealed class ClaimSlaTests
{
    private static readonly DateTimeOffset Created = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public void RecordsOnlyAfterFortyEightHoursAndAtMostOncePerTwentyFourHours()
    {
        var claim = Report();
        Assert.False(claim.TryRecordSlaBreach(Created.AddHours(48)));
        var breach = Created.AddHours(48).AddTicks(1);
        Assert.True(claim.TryRecordSlaBreach(breach));
        Assert.False(claim.TryRecordSlaBreach(breach.AddHours(24).AddTicks(-1)));
        Assert.True(claim.TryRecordSlaBreach(breach.AddHours(24)));
        Assert.Equal(ClaimStatus.Draft, claim.Status);
        Assert.Null(claim.UpdatedAt);
    }

    [Fact]
    public void ActivityClearsTheFlagUntilTheNextBreachAndOnlyDraftAndOpenAreTracked()
    {
        var claim = Report();
        var breach = Created.AddHours(49);
        Assert.True(claim.TryRecordSlaBreach(breach));
        Assert.True(claim.IsSlaBreached(breach));
        var activity = breach.AddMinutes(1);
        claim.RecordActivity(Actor, activity);
        Assert.False(claim.IsSlaBreached(activity.AddHours(49)));
        Assert.True(claim.TryRecordSlaBreach(activity.AddHours(49)));
        var details = new ClaimTransitionDetails(null, Guid.NewGuid(), false, false, false);
        claim.ChangeStatus(ClaimStatus.Open, details, new(true, default), Actor, activity.AddHours(50));
        claim.ChangeStatus(ClaimStatus.UnderInvestigation, details with { AssignedHandlerId = null },
            new(true, default), Actor, activity.AddHours(51));
        Assert.False(claim.TryRecordSlaBreach(activity.AddDays(7)));
        Assert.False(claim.IsSlaBreached(activity.AddDays(7)));
    }

    [Fact]
    public async Task AFailedClaimDoesNotBlockTheRestOfAnOrganizationScan()
    {
        var organization = Guid.NewGuid();
        var requests = new TestSlaRequests(organization);
        var services = new ServiceCollection();
        services.AddMediatR(options => options.RegisterServicesFromAssemblyContaining<ClaimSlaTests>());
        services.AddScoped<BackgroundTenant>();
        services.AddSingleton<IRequestHandler<ListSlaCandidatesQuery, IReadOnlyList<Guid>>>(requests);
        services.AddSingleton<IRequestHandler<DetectClaimSlaBreachCommand, bool>>(requests);
        using var provider = services.BuildServiceProvider();
        var runner = new BackgroundCommandRunner(provider.GetRequiredService<IServiceScopeFactory>());
        var job = new SlaMonitoringJob(runner, new ReservePostingRecoveryOptions([organization]), TimeProvider.System,
            NullLogger<SlaMonitoringJob>.Instance);
        await job.ScanAsync(Guid.NewGuid(), default);
        Assert.Equal(0, requests.Scans);
        await job.ScanAsync(organization, default);
        Assert.Equal(1, requests.Scans);
        Assert.Equal(requests.SecondClaim, Assert.Single(requests.Completed));
    }

    private static Claim Report() => Claim.Report(Guid.NewGuid(), Actor, Created, 1,
        new FnolDetails("Property", new LossDetails(Created.AddHours(-1), "FIRE", "A qualifying property loss occurred."),
            [new PartyDetails(ClaimPartyRole.Claimant, ClaimPartyType.Person, "Ada", "Gray")], []), null, true);

    private sealed class TestSlaRequests(Guid organization) : IRequestHandler<ListSlaCandidatesQuery, IReadOnlyList<Guid>>,
        IRequestHandler<DetectClaimSlaBreachCommand, bool>
    {
        public Guid SecondClaim { get; } = Guid.NewGuid();
        public List<Guid> Completed { get; } = [];
        public int Scans { get; private set; }

        public Task<IReadOnlyList<Guid>> Handle(ListSlaCandidatesQuery request, CancellationToken cancellationToken)
        {
            Scans++;
            return Task.FromResult<IReadOnlyList<Guid>>([organization, SecondClaim]);
        }

        public Task<bool> Handle(DetectClaimSlaBreachCommand request, CancellationToken cancellationToken)
        {
            if (request.ClaimId == organization)
            {
                throw new InvalidOperationException("The first claim could not be checked.");
            }

            Completed.Add(request.ClaimId);
            return Task.FromResult(true);
        }
    }
}
