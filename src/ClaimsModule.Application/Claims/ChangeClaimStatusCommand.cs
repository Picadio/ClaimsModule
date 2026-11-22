using ClaimsModule.Application.Exceptions;
using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Exceptions;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Claims;

public sealed record ChangeClaimStatusRequest(ClaimStatus? TargetStatus, string? Reason = null,
    Guid? AssignedHandlerId = null, bool AcknowledgePolicyPeriodWarning = false,
    bool ConfirmPositiveReserveBalance = false, bool LiabilityDetermined = false);

public sealed record ClaimStatusResult(Guid Id, ClaimStatus Status, Guid? AssignedHandlerId, DateTimeOffset? ClosedAt,
    string? ClosureReason);

public sealed record ChangeClaimStatusCommand(Guid Id, ChangeClaimStatusRequest Request, string? IdempotencyKey,
    Guid CorrelationId) : IRequest<ClaimStatusResult?>;

internal sealed class ChangeClaimStatusValidator : AbstractValidator<ChangeClaimStatusCommand>
{
    public ChangeClaimStatusValidator()
    {
        RuleFor(command => command.Request.TargetStatus).NotNull().IsInEnum();
        RuleFor(command => command.Request.AssignedHandlerId).NotEqual(Guid.Empty);
        RuleFor(command => command.Request.Reason).MaximumLength(2000);
        RuleFor(command => command.IdempotencyKey).MaximumLength(255)
            .Must(key => key is null || !string.IsNullOrWhiteSpace(key))
            .WithMessage("Idempotency-Key cannot be blank.");
    }
}

internal sealed class ChangeClaimStatusHandler(IClaimLifecycleStore lifecycle, IClaimStore claims,
    IClaimUnitOfWork unitOfWork, ICurrentUser currentUser, IClaimHandlerDirectory handlers,
    ClaimLifecycleOptions options, IPublisher publisher, TimeProvider clock)
    : IRequestHandler<ChangeClaimStatusCommand, ClaimStatusResult?>
{
    public Task<ClaimStatusResult?> Handle(ChangeClaimStatusCommand command, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteClaimWriteAsync<ClaimStatusResult?>(command.Id, async token =>
        {
            var claim = await lifecycle.FindForUpdateAsync(command.Id, token);
            if (claim is null)
            {
                return null;
            }

            var user = currentUser.Identity;
            var request = command.Request;
            var keyHash = IdempotencyFingerprint.HashKey(command.IdempotencyKey);
            var requestHash = keyHash is null ? null : IdempotencyFingerprint.HashRequest(request);
            if (keyHash is not null &&
                await lifecycle.FindStatusChangeAsync(claim.Id, user.UserId, keyHash, token) is { } prior)
            {
                return prior.RequestHash == requestHash ? prior.Response : throw new IdempotencyConflictException();
            }

            var target = request.TargetStatus!.Value;
            var rules = await lifecycle.ListTransitionsAsync(token);
            if (ClaimTransitionRule.CheckTransition(rules, user.Role, options, claim.Status, target) is { } issue)
            {
                throw new ClaimRuleViolation(issue.Field, issue.Message);
            }

            if (request.AssignedHandlerId is Guid handler && !await handlers.IsHandlerAsync(user.OrganizationEntityId, handler, token))
            {
                throw new ClaimRuleViolation("AssignedHandlerId", "Select a handler from this organization.");
            }

            var causeActive = (target is not (ClaimStatus.Open or ClaimStatus.Reopened or ClaimStatus.Closed)) ||
                (claim.LossEvent is not null && await claims.IsCauseActiveAsync(claim.LossEvent.CauseOfLossCode, token));
            var reserves = target is ClaimStatus.PendingPayment or ClaimStatus.Closed
                ? await lifecycle.ReadReserveFactsAsync(claim.Id, token) : default;
            var now = clock.GetUtcNow();
            var change = claim.ChangeStatus(target, new ClaimTransitionDetails(request.Reason, request.AssignedHandlerId,
                request.AcknowledgePolicyPeriodWarning, request.ConfirmPositiveReserveBalance, request.LiabilityDetermined),
                new ClaimLifecycleFacts(causeActive, reserves), user.UserId, now);
            // Lock the claim row before inserting audit entries, matching the audit reader's lock order.
            await unitOfWork.SaveAsync(token);
            await publisher.Publish(new ClaimStatusChangedNotification(claim, change, user.UserId, now,
                command.CorrelationId), token);

            var response = new ClaimStatusResult(claim.Id, claim.Status, claim.AssignedHandlerId, claim.ClosedAt,
                claim.ClosureReason);
            if (keyHash is not null)
            {
                lifecycle.AddStatusChange(new StoredStatusChange(user.UserId, now, keyHash, requestHash!, response));
            }

            await unitOfWork.SaveAsync(token);
            return response;
        }, cancellationToken);
}
