using ClaimsModule.Application.Reserves;
using Hangfire;
using Hangfire.States;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClaimsModule.Infrastructure.Jobs;

internal sealed record ReservePostingRecoveryOptions(IReadOnlyList<Guid> Organizations);

internal sealed class ReservePostingRecovery(ReservePostingRecoveryOptions options, BackgroundCommandRunner runner,
    JobStorage storage, ILogger<ReservePostingRecovery> logger) : BackgroundService
{
    private const int PageSize = 100;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(options.Organizations.Select(organization => RunOrganizationAsync(organization, stoppingToken)));

    private async Task RunOrganizationAsync(Guid organization, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        Guid? cursor = null;
        Guid? watermark = null;
        do
        {
            try
            {
                watermark ??= await runner.SendAsync(organization, new FindPostingRecoveryWatermarkQuery(), stoppingToken);
                if (watermark is null)
                {
                    continue;
                }

                var page = await runner.SendAsync(organization,
                    new ListPendingReservePostingsQuery(cursor, PageSize, watermark), stoppingToken);
                foreach (var candidate in page)
                {
                    await RecoverCandidateAsync(organization, candidate, stoppingToken);
                }

                cursor = page.Count == PageSize ? page[^1].Target.TransactionId : null;
                if (cursor is null || cursor == watermark)
                {
                    cursor = null;
                    watermark = null;
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Could not recover GL postings for organization {OrganizationId}.",
                    organization);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RecoverCandidateAsync(Guid organization, ReservePostingCandidate candidate,
        CancellationToken cancellationToken)
    {
        try
        {
            if (candidate.JobId is not { } jobId)
            {
                await runner.SendAsync(organization, new DispatchReservePostingCommand(candidate.Target), cancellationToken);
                return;
            }

            var reason = ReadFailureReason(jobId);
            if (reason is not null)
            {
                await runner.SendAsync(organization, new FinishReservePostingCommand(candidate.Target, jobId, reason),
                    cancellationToken);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not recover GL posting for reserve transaction {TransactionId}.",
                candidate.Target.TransactionId);
        }
    }

    private string? ReadFailureReason(string jobId)
    {
        using var connection = storage.GetConnection();
        var state = connection.GetStateData(jobId);
        return state?.Name switch
        {
            var name when name == FailedState.StateName => state!.Data.TryGetValue("ExceptionType", out var type)
                ? $"Hangfire exhausted retries after {type}."
                : "Hangfire exhausted the automatic GL posting retries.",
            var name when name == DeletedState.StateName => "The Hangfire posting job was deleted.",
            var name when name == SucceededState.StateName => "The Hangfire posting job finished without recording a GL posting.",
            null => "The assigned GL posting job no longer exists in Hangfire.",
            _ => null
        };
    }
}
