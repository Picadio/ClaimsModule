using ClaimsModule.Application.Reserves;
using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.Logging;
using MediatR;

namespace ClaimsModule.Infrastructure.Jobs;

internal sealed class HangfireReservePostingQueue(IBackgroundJobClient jobs) : IReservePostingQueue
{
    public string Enqueue(ReservePostingTarget target) => jobs.Enqueue<GlPostingJob>(
        job => job.PostAsync(target, null!, CancellationToken.None));
}

internal sealed class GlPostingJob(BackgroundCommandRunner runner)
{
    public async Task PostAsync(ReservePostingTarget target, PerformContext context, CancellationToken cancellationToken)
    {
        await runner.SendAsync(target.OrganizationId, new FinishReservePostingCommand(target, context.BackgroundJob.Id),
            cancellationToken);
    }
}

internal sealed class DispatchAfterApproval(BackgroundCommandRunner runner, ILogger<DispatchAfterApproval> logger)
    : INotificationHandler<ReservePostingReady>
{
    public async Task Handle(ReservePostingReady notification, CancellationToken cancellationToken)
    {
        var target = notification.Target;
        try
        {
            await runner.SendAsync(target.OrganizationId, new DispatchReservePostingCommand(target), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Recovery can dispatch the committed posting.
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "GL dispatch for reserve transaction {TransactionId} will be retried by recovery.",
                target.TransactionId);
        }
    }
}
