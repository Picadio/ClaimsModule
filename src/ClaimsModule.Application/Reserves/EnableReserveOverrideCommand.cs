using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Exceptions;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Reserves;

public sealed record EnableReserveOverrideCommand(Guid ClaimId, EnableReserveOverrideRequest Request,
    string? IdempotencyKey, Guid CorrelationId) : IRequest<ReserveOverrideResult?>;

internal sealed class EnableReserveOverrideValidator : AbstractValidator<EnableReserveOverrideCommand>
{
    public EnableReserveOverrideValidator()
    {
        RuleFor(command => command.Request.Reason).NotEmpty();
        RuleFor(command => command.IdempotencyKey).MaximumLength(255)
            .Must(key => key is null || !string.IsNullOrWhiteSpace(key))
            .WithMessage("Idempotency-Key cannot be blank.");
    }
}

internal sealed class EnableReserveOverrideHandler(IReserveStore reserves, IClaimStore claims, IClaimUnitOfWork unitOfWork,
    ICurrentUser currentUser, TimeProvider clock, ReserveReplays replays, IPublisher publisher)
    : IRequestHandler<EnableReserveOverrideCommand, ReserveOverrideResult?>
{
    public Task<ReserveOverrideResult?> Handle(EnableReserveOverrideCommand command, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteClaimWriteAsync<ReserveOverrideResult?>(command.ClaimId, async token =>
        {
            var user = currentUser.Identity;
            if (user.Role != UserRoles.Manager)
            {
                throw new ClaimRuleViolation("Role", "Only a manager can enable the reserve limit override.");
            }

            var claim = await reserves.FindClaimForUpdateAsync(command.ClaimId, token);
            if (claim is null)
            {
                return null;
            }

            var key = ReserveReplayKey.Create(command.IdempotencyKey, new { Action = "EnableReserveOverride", command.Request });
            if (await replays.ReadAsync<ReserveOverrideResult>(claim.Id, user.UserId, key, token) is { } prior)
            {
                return prior;
            }

            claim.EnsureCanSubmitReserve();
            if (await claims.FindPolicyAsync(claim.PolicyId!.Value, token) is null)
            {
                throw new ClaimRuleViolation("PolicyId", "Link a policy before enabling the reserve limit override.");
            }

            var now = clock.GetUtcNow();
            if (claim.EnableReserveLimitOverride(user.UserId, now))
            {
                await unitOfWork.SaveAsync(token);
                await publisher.Publish(new ReserveOverrideEnabled(claim.OrganizationEntityId, claim.Id, user.UserId, now,
                    command.CorrelationId, command.Request.Reason!.Trim()), token);
            }

            var result = new ReserveOverrideResult(claim.Id, claim.ReserveLimitOverrideEnabled);
            replays.Record(claim.Id, user.UserId, now, key, result);
            await unitOfWork.SaveAsync(token);
            return result;
        }, cancellationToken);
}

internal sealed record ReserveOverrideEnabled(Guid OrganizationId, Guid ClaimId, Guid ActorId, DateTimeOffset OccurredAt,
    Guid CorrelationId, string Reason) : INotification;

internal sealed class RecordReserveOverrideAudit(IAuditLogService audit) : INotificationHandler<ReserveOverrideEnabled>
{
    public Task Handle(ReserveOverrideEnabled notification, CancellationToken cancellationToken)
    {
        var (organizationId, claimId, actorId, occurredAt, correlationId, reason) = notification;
        audit.Append(new ClaimAuditEvent(organizationId, claimId, actorId, occurredAt, correlationId,
            "RESERVE_LIMIT_OVERRIDE_ENABLED", "A manager enabled the reserve limit override.",
            new { ReserveLimitOverrideEnabled = false }, new { ReserveLimitOverrideEnabled = true, Reason = reason }));
        return Task.CompletedTask;
    }
}
