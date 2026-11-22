using ClaimsModule.Application.Claims;
using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClaimsModule.Infrastructure.Jobs;

internal sealed class SlaMonitoringJob(BackgroundCommandRunner runner, ReservePostingRecoveryOptions options,
    TimeProvider clock, ILogger<SlaMonitoringJob> logger)
{
    private const int PageSize = 100;

    public async Task ScanAsync(Guid organization, CancellationToken cancellationToken)
    {
        if (!options.Organizations.Contains(organization))
        {
            return;
        }

        var now = clock.GetUtcNow();
        var correlationId = Guid.NewGuid();
        Guid? cursor = null;
        IReadOnlyList<Guid> page;
        do
        {
            page = await runner.SendAsync(organization, new ListSlaCandidatesQuery(now, cursor, PageSize), cancellationToken);
            foreach (var claimId in page)
            {
                try
                {
                    await runner.SendAsync(organization, new DetectClaimSlaBreachCommand(claimId, now, correlationId),
                        cancellationToken);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(exception, "Could not check SLA for claim {ClaimId}.", claimId);
                }

                cursor = claimId;
            }
        } while (page.Count == PageSize);
    }
}

internal sealed class SlaJobRegistration(IRecurringJobManager jobs, ReservePostingRecoveryOptions options,
    ILogger<SlaJobRegistration> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var organization in options.Organizations)
                {
                    jobs.AddOrUpdate($"claims-sla-{organization:D}",
                        Job.FromExpression<SlaMonitoringJob>(job => job.ScanAsync(organization, CancellationToken.None)),
                        "*/15 * * * *", new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
                }

                return;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Could not register recurring SLA checks. Registration will retry.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
