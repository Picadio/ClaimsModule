using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Exceptions;
using ClaimsModule.Domain.Reserves;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Reserves;

public enum ReserveDecision { Approve = 1, Reject, Retract }

public sealed record ResolveReserveCommand(Guid ClaimId, Guid TransactionId, ReserveDecision Decision,
    string? RejectionReason, string? IdempotencyKey, Guid CorrelationId) : IRequest<ReserveTransaction?>;

internal sealed class ResolveReserveValidator : AbstractValidator<ResolveReserveCommand>
{
    public ResolveReserveValidator()
    {
        RuleFor(command => command.Decision).IsInEnum();
        RuleFor(command => command.RejectionReason).NotEmpty().When(command => command.Decision == ReserveDecision.Reject);
        RuleFor(command => command.IdempotencyKey).MaximumLength(255)
            .Must(key => key is null || !string.IsNullOrWhiteSpace(key))
            .WithMessage("Idempotency-Key cannot be blank.");
    }
}

internal sealed class ResolveReserveHandler(IReserveStore reserves, IClaimStore claims, IClaimUnitOfWork unitOfWork,
    ICurrentUser currentUser, TimeProvider clock, ReserveReplays replays, IPublisher publisher)
    : IRequestHandler<ResolveReserveCommand, ReserveTransaction?>
{
    public async Task<ReserveTransaction?> Handle(ResolveReserveCommand command, CancellationToken cancellationToken)
    {
        var user = currentUser.Identity;
        var transaction = await unitOfWork.ExecuteClaimWriteAsync<ReserveTransaction?>(command.ClaimId, async token =>
        {
            var claim = await reserves.FindClaimForUpdateAsync(command.ClaimId, token);
            if (claim is null ||
                await reserves.FindHistoryAsync(claim.Id, command.TransactionId, token) is not { } entry ||
                await reserves.FindComponentAsync(claim.Id, entry.ReserveId, null, token) is not { } component)
            {
                return null;
            }

            var key = ReserveReplayKey.Create(command.IdempotencyKey,
                new { command.TransactionId, command.Decision, command.RejectionReason });
            if (await replays.ReadAsync<ReserveTransaction>(claim.Id, user.UserId, key, token) is { } prior)
            {
                return prior;
            }

            var authority = user.Role switch
            {
                UserRoles.Manager => ReserveApprovalAuthority.Manager,
                UserRoles.Supervisor => ReserveApprovalAuthority.Supervisor,
                _ => ReserveApprovalAuthority.Automatic
            };
            var now = clock.GetUtcNow();
            claim.RecordActivity(user.UserId, now);
            await unitOfWork.SaveAsync(token);

            var previousBalance = component.CurrentAmount;
            switch (command.Decision)
            {
                case ReserveDecision.Approve:
                    claim.EnsureCanSubmitReserve();
                    if (await claims.FindPolicyAsync(claim.PolicyId!.Value, token) is null)
                    {
                        throw new ClaimRuleViolation("PolicyId", "Link a policy before approving a reserve.");
                    }

                    var total = await reserves.SumApprovedAsync(claim.Id, token);
                    component.Approve(entry, authority, total, claim.ReserveLimitOverrideEnabled, user.UserId, now);
                    entry.CapturePostingCorrelation(command.CorrelationId);
                    break;
                case ReserveDecision.Reject:
                    entry.Reject(authority, user.UserId, now, command.RejectionReason);
                    break;
                case ReserveDecision.Retract:
                    entry.Retract(user.UserId, now);
                    break;
                default:
                    throw new ClaimRuleViolation("Decision", "Select a valid reserve decision.");
            }

            var result = ReserveTransaction.From(entry, component.ComponentType);
            await publisher.Publish(new ReserveResolved(claim.OrganizationEntityId, claim.Id, user.UserId, now,
                command.CorrelationId, command.Decision, previousBalance, component.CurrentAmount, result), token);
            replays.Record(claim.Id, user.UserId, now, key, result);
            await unitOfWork.SaveAsync(token);
            return result;
        }, cancellationToken);

        await ReservePostingReady.RequestAsync(publisher, user.OrganizationEntityId, command.ClaimId, transaction,
            cancellationToken);
        return transaction;
    }
}

internal sealed record ReserveResolved(Guid OrganizationId, Guid ClaimId, Guid ActorId, DateTimeOffset OccurredAt,
    Guid CorrelationId, ReserveDecision Decision, decimal PreviousBalance, decimal CurrentBalance,
    ReserveTransaction Transaction) : INotification;

internal sealed class RecordReserveDecisionAudit(IAuditLogService audit) : INotificationHandler<ReserveResolved>
{
    public Task Handle(ReserveResolved notification, CancellationToken cancellationToken)
    {
        var (organizationId, claimId, actorId, now, correlation, decision, previous, current, transaction) = notification;
        var (type, description) = decision switch
        {
            ReserveDecision.Approve => ("RESERVE_APPROVED", "A reserve transaction was approved."),
            ReserveDecision.Reject => ("RESERVE_REJECTED", "A reserve transaction was rejected."),
            ReserveDecision.Retract => ("RESERVE_RETRACTED", "A reserve transaction was retracted."),
            _ => throw new ArgumentOutOfRangeException(nameof(notification))
        };

        audit.Append(new ClaimAuditEvent(organizationId, claimId, actorId, now, correlation, type, description,
            new { ApprovalStatus = nameof(ReserveApprovalStatus.PendingApproval), Balance = previous, transaction.RejectionReason },
            new { ApprovalStatus = transaction.ApprovalStatus.ToString(), Balance = current, transaction.Amount,
                PostingStatus = transaction.PostingStatus.ToString() }, transaction.Id, nameof(ReserveHistory)));
        return Task.CompletedTask;
    }
}
