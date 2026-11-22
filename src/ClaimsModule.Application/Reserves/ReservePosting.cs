using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Reserves;
using MediatR;

namespace ClaimsModule.Application.Reserves;

public sealed record ReservePostingTarget(Guid OrganizationId, Guid ClaimId, Guid TransactionId, string PostingKey);

public sealed record ReservePostingCandidate(ReservePostingTarget Target, string? JobId);

public interface IReservePostingQueue
{
    string Enqueue(ReservePostingTarget target);
}

public sealed record ReservePostingReady(ReservePostingTarget Target) : INotification
{
    internal static Task RequestAsync(IPublisher publisher, Guid organizationId, Guid claimId,
        ReserveTransaction? transaction, CancellationToken cancellationToken) =>
        transaction is
        {
            ApprovalStatus: ReserveApprovalStatus.AutoApproved or ReserveApprovalStatus.Approved,
            PostingStatus: ReservePostingStatus.Pending
        }
            ? publisher.Publish(new ReservePostingReady(new ReservePostingTarget(organizationId, claimId,
                transaction.Id, transaction.PostingKey)), cancellationToken)
            : Task.CompletedTask;
}

public sealed record FindPostingRecoveryWatermarkQuery : IRequest<Guid?>;

public sealed record ListPendingReservePostingsQuery(Guid? AfterId, int PageSize, Guid? ThroughId = null)
    : IRequest<IReadOnlyList<ReservePostingCandidate>>;

internal sealed class ListPendingReservePostingsHandler(IReserveStore reserves)
    : IRequestHandler<ListPendingReservePostingsQuery, IReadOnlyList<ReservePostingCandidate>>,
      IRequestHandler<FindPostingRecoveryWatermarkQuery, Guid?>
{
    public Task<Guid?> Handle(FindPostingRecoveryWatermarkQuery query, CancellationToken cancellationToken) =>
        reserves.FindLastPendingPostingIdAsync(cancellationToken);

    public Task<IReadOnlyList<ReservePostingCandidate>> Handle(ListPendingReservePostingsQuery query,
        CancellationToken cancellationToken) =>
        reserves.ListPendingPostingsAsync(query.AfterId, query.PageSize, query.ThroughId, cancellationToken);
}

public sealed record DispatchReservePostingCommand(ReservePostingTarget Target) : IRequest<bool>;

public sealed record FinishReservePostingCommand(ReservePostingTarget Target, string JobId, string? FailureReason = null)
    : IRequest<bool>;

internal sealed class ReservePostingHandler(IReserveStore reserves, IClaimUnitOfWork unitOfWork,
    IAuditLogService audit, TimeProvider clock, IReservePostingQueue? queue = null)
    : IRequestHandler<DispatchReservePostingCommand, bool>, IRequestHandler<FinishReservePostingCommand, bool>
{
    public Task<bool> Handle(DispatchReservePostingCommand command, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteClaimJobAsync(command.Target.ClaimId, async token =>
        {
            var entry = await FindPendingAsync(command.Target, token);
            if (entry is null || entry.PostingJobId is not null || queue is null)
            {
                return false;
            }

            var jobId = queue.Enqueue(command.Target);
            entry.AssignPostingJob(jobId, Guid.NewGuid());
            await unitOfWork.SaveAsync(token);
            return true;
        }, cancellationToken);

    public Task<bool> Handle(FinishReservePostingCommand command, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteClaimJobAsync(command.Target.ClaimId, async token =>
        {
            var entry = await FindPendingAsync(command.Target, token);
            var succeeded = command.FailureReason is null;
            if (entry is null || !entry.TryFinishPosting(command.JobId, command.Target.PostingKey, succeeded))
            {
                return false;
            }

            await unitOfWork.SaveAsync(token);
            AppendResult(entry, command.FailureReason, clock.GetUtcNow());
            await unitOfWork.SaveAsync(token);
            return true;
        }, cancellationToken);

    private async Task<ReserveHistory?> FindPendingAsync(ReservePostingTarget target, CancellationToken cancellationToken)
    {
        var claim = await reserves.FindClaimForUpdateAsync(target.ClaimId, cancellationToken);
        if (claim is null || claim.OrganizationEntityId != target.OrganizationId ||
            await reserves.FindHistoryAsync(target.ClaimId, target.TransactionId, cancellationToken) is not
                { IsPendingPosting: true } entry ||
            entry.PostingKey != target.PostingKey ||
            await reserves.FindComponentAsync(target.ClaimId, entry.ReserveId, null, cancellationToken) is null)
        {
            return null;
        }

        return entry;
    }

    private void AppendResult(ReserveHistory entry, string? failureReason, DateTimeOffset now)
    {
        var succeeded = failureReason is null;
        var type = succeeded ? "GL_POSTING_SIMULATED" : "GL_POSTING_FAILED";
        var description = succeeded ? "A reserve GL posting was simulated." : "The reserve GL posting failed.";
        object values = succeeded
            ? SimulatedGlJournal.For(entry.Amount, entry.PostingKey, entry.PostingJobId!)
            : new { Reason = failureReason, entry.PostingKey, entry.PostingJobId };
        audit.Append(new ClaimAuditEvent(entry.OrganizationEntityId, entry.ClaimId, null, now,
            entry.PostingCorrelationId ?? Guid.NewGuid(), type, description,
            NewValues: values, RelatedEntityId: entry.Id, RelatedEntityType: nameof(ReserveHistory)));
    }
}

public sealed record SimulatedGlJournal(string DebitAccount, string CreditAccount, decimal Amount,
    string PostingKey, string PostingJobId)
{
    public static SimulatedGlJournal For(decimal amount, string postingKey, string jobId)
    {
        const string change = "Change in Outstanding Reserves";
        const string outstanding = "Outstanding Loss Reserves";
        return amount > 0 ? new(change, outstanding, amount, postingKey, jobId)
            : new(outstanding, change, -amount, postingKey, jobId);
    }
}
