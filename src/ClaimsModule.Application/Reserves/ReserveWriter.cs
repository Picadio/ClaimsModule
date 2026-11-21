using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Exceptions;
using ClaimsModule.Domain.Reserves;
using MediatR;

namespace ClaimsModule.Application.Reserves;

internal sealed class ReserveWriter(IReserveStore store, IClaimUnitOfWork unitOfWork, IPublisher publisher)
{
    public async Task<ReserveTransaction> SubmitAsync(Claim claim, ClaimReserveComponent? component,
        ReserveComponentType componentType, ReserveChange change, Guid actorId, DateTimeOffset occurredAt,
        Guid correlationId, int auditPosition, CancellationToken cancellationToken, ReserveHistory? replaces = null)
    {
        claim.EnsureCanSubmitReserve();
        if (component is null && change.TransactionType != ReserveTransactionType.Add)
        {
            throw new ClaimRuleViolation("TransactionType", "Open the component before adjusting or reversing it.");
        }

        if (component is null)
        {
            component = new ClaimReserveComponent(claim.OrganizationEntityId, claim.Id, componentType, actorId, occurredAt);
            store.AddComponent(component);
            await unitOfWork.SaveAsync(cancellationToken);
        }

        var currentTotal = await store.SumApprovedAsync(claim.Id, cancellationToken);
        var hasPendingAdd = change.TransactionType == ReserveTransactionType.Add &&
            await store.HasPendingAddAsync(claim.Id, component.Id, cancellationToken);
        var entry = component.Submit(change, currentTotal, hasPendingAdd, actorId, occurredAt,
            claim.ReserveLimitOverrideEnabled, replaces);
        store.AddHistory(entry);
        await unitOfWork.SaveAsync(cancellationToken);

        var result = ReserveTransaction.From(entry, componentType);
        await publisher.Publish(new ReserveSubmitted(claim.OrganizationEntityId, claim.Id, correlationId,
            auditPosition, result), cancellationToken);
        return result;
    }
}

internal sealed record ReserveSubmitted(Guid OrganizationId, Guid ClaimId, Guid CorrelationId, int Position,
    ReserveTransaction Transaction) : INotification;

internal sealed class RecordReserveAudit(IAuditLogService audit) : INotificationHandler<ReserveSubmitted>
{
    public Task Handle(ReserveSubmitted notification, CancellationToken cancellationToken)
    {
        var (organizationId, claimId, correlation, position, transaction) = notification;
        var data = new
        {
            transaction.ReserveId, Component = transaction.Component.ToString(), transaction.Amount,
            transaction.NewBalance, transaction.ChangeReason, transaction.ReplacesRejectedTransactionId,
            ApprovalStatus = transaction.ApprovalStatus.ToString()
        };
        audit.Append(new ClaimAuditEvent(organizationId, claimId, transaction.SubmittedByUserId, transaction.CreatedAt,
            correlation, "RESERVE_CREATED", "A reserve transaction was submitted.", new { transaction.PreviousBalance },
            data, transaction.Id, nameof(ReserveHistory), position++));

        if (transaction.ApprovalStatus == ReserveApprovalStatus.AutoApproved)
        {
            audit.Append(new ClaimAuditEvent(organizationId, claimId, transaction.SubmittedByUserId, transaction.CreatedAt,
                correlation, "RESERVE_AUTO_APPROVED", "The reserve transaction was automatically approved.",
                NewValues: data, RelatedEntityId: transaction.Id, RelatedEntityType: nameof(ReserveHistory), Position: position));
        }
        else if (transaction.RequiresManagerOverride)
        {
            audit.Append(new ClaimAuditEvent(organizationId, claimId, transaction.SubmittedByUserId, transaction.CreatedAt,
                correlation, "VALIDATION_ISSUE_ADDED", ReserveTransaction.LimitWarningMessage,
                NewValues: new { Code = ReserveTransaction.LimitWarningCode, Severity = "Warning" },
                RelatedEntityId: transaction.Id, RelatedEntityType: nameof(ReserveHistory), Position: position));
        }

        return Task.CompletedTask;
    }
}
