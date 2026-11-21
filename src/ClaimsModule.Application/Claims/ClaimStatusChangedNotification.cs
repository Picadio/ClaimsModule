using ClaimsModule.Domain.Claims;
using MediatR;

namespace ClaimsModule.Application.Claims;

internal sealed record ClaimStatusChangedNotification(Claim Claim, ClaimStatusChange Change, Guid ActorId,
    DateTimeOffset OccurredAt, Guid CorrelationId) : INotification;

internal sealed class RecordClaimStatusAudit(IAuditLogService audit) : INotificationHandler<ClaimStatusChangedNotification>
{
    public Task Handle(ClaimStatusChangedNotification notification, CancellationToken cancellationToken)
    {
        var (claim, change, actorId, occurredAt, correlationId) = notification;
        var position = 0;

        if (change.PreviousHandlerId != claim.AssignedHandlerId)
        {
            Append("HANDLER_ASSIGNED", "A handler was assigned to the claim.",
                new { AssignedHandlerId = change.PreviousHandlerId }, new { claim.AssignedHandlerId });
        }

        if (change.AcknowledgedWarning is { } warning)
        {
            Append("VALIDATION_WARNING_ACKNOWLEDGED", warning.Message, null, new { warning.Code, warning.Field },
                warning.Id, nameof(ClaimWarning));
        }

        RecordTransition(change.PreviousStatus, change.TargetStatus, change.Reason, change.LiabilityDetermined);
        if (change.TargetStatus == ClaimStatus.Closed)
        {
            Append("CLAIM_CLOSED", "The claim was closed.", null,
                new { claim.ClosureReason, change.PositiveBalanceConfirmed });
        }
        else if (change.TargetStatus == ClaimStatus.Reopened)
        {
            Append("CLAIM_REOPENED", "The claim was reopened.", null, new { change.Reason });
            RecordTransition(ClaimStatus.Reopened, change.FinalStatus);
        }

        return Task.CompletedTask;

        void RecordTransition(ClaimStatus from, ClaimStatus to, string? reason = null, bool liabilityDetermined = false) =>
            Append("STATUS_CHANGED", $"Claim status changed from {from} to {to}.",
                new { Status = from.ToString() }, new { Status = to.ToString(), Reason = reason, LiabilityDetermined = liabilityDetermined });

        void Append(string type, string description, object? oldValues, object? newValues,
            Guid? relatedId = null, string? relatedType = null) =>
            audit.Append(new ClaimAuditEvent(claim.OrganizationEntityId, claim.Id, actorId, occurredAt, correlationId,
                type, description, oldValues, newValues, relatedId, relatedType, position++));
    }
}
