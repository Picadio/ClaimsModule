using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Exceptions;
using ClaimsModule.Domain.Reserves;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Reserves;

public sealed record SubmitReserveCommand(Guid ClaimId, Guid? ReserveId, SubmitReserveRequest Request,
    string? IdempotencyKey, Guid CorrelationId) : IRequest<ReserveTransaction?>;

internal sealed class SubmitReserveValidator : AbstractValidator<SubmitReserveCommand>
{
    public SubmitReserveValidator()
    {
        RuleFor(command => command.Request.Component).NotNull().IsInEnum()
            .When(command => !command.ReserveId.HasValue);
        RuleFor(command => command.Request.TransactionType).IsInEnum();
        RuleFor(command => command.Request.ReplacesRejectedTransactionId).NotEqual(Guid.Empty);
        RuleFor(command => command.Request.TransactionType).NotEqual(ReserveTransactionType.Add)
            .When(command => command.ReserveId.HasValue);
        RuleFor(command => command.Request.Amount).NotEqual(0m).PrecisionScale(19, 4, true);
        RuleFor(command => command.Request.ChangeReason).NotEmpty()
            .Must(reason => reason is null || reason.Trim().Length <= ClaimReserveComponent.ChangeReasonMaximumLength)
            .WithMessage("ChangeReason cannot exceed 500 characters.");
        RuleFor(command => command.IdempotencyKey).MaximumLength(255)
            .Must(key => key is null || !string.IsNullOrWhiteSpace(key))
            .WithMessage("Idempotency-Key cannot be blank.");
    }
}

internal sealed class SubmitReserveHandler(IReserveStore reserves, IClaimStore claims, IClaimUnitOfWork unitOfWork,
    ICurrentUser currentUser, TimeProvider clock, ReserveWriter writer, ReserveReplays replays, IPublisher publisher)
    : IRequestHandler<SubmitReserveCommand, ReserveTransaction?>
{
    public async Task<ReserveTransaction?> Handle(SubmitReserveCommand command, CancellationToken cancellationToken)
    {
        var user = currentUser.Identity;
        var transaction = await unitOfWork.ExecuteClaimWriteAsync<ReserveTransaction?>(command.ClaimId, async token =>
        {
            var claim = await reserves.FindClaimForUpdateAsync(command.ClaimId, token);
            if (claim is null)
            {
                return null;
            }

            var request = command.Request;
            var component = await reserves.FindComponentAsync(claim.Id, command.ReserveId, request.Component, token);
            if (command.ReserveId.HasValue && component is null)
            {
                return null;
            }

            if (component is null && await reserves.IsComponentArchivedAsync(claim.Id, request.Component!.Value, token))
            {
                throw new ClaimRuleViolation("Component", "This claim already has an archived reserve of this type.");
            }

            var key = ReserveReplayKey.Create(command.IdempotencyKey, new { command.ReserveId, request });
            if (await replays.ReadAsync<ReserveTransaction>(claim.Id, user.UserId, key, token) is { } prior)
            {
                return prior;
            }

            claim.EnsureCanSubmitReserve();
            if (await claims.FindPolicyAsync(claim.PolicyId!.Value, token) is null)
            {
                throw new ClaimRuleViolation("PolicyId", "Link a policy before submitting a reserve.");
            }

            ReserveHistory? replaces = null;
            if (request.ReplacesRejectedTransactionId is Guid rejectedId)
            {
                replaces = await reserves.FindHistoryAsync(claim.Id, rejectedId, token)
                    ?? throw new ClaimRuleViolation("ReplacesRejectedTransactionId", "Select a rejected transaction on this claim.");
                if (await reserves.HasUncancelledRevisionAsync(claim.Id, rejectedId, token))
                {
                    throw new ClaimRuleViolation("ReplacesRejectedTransactionId",
                        "This transaction already has a revision. Use the latest rejected transaction for a further revision.");
                }
            }

            var now = clock.GetUtcNow();
            claim.RecordActivity(user.UserId, now);
            await unitOfWork.SaveAsync(token);

            var result = await writer.SubmitAsync(claim, component, component?.ComponentType ?? request.Component!.Value,
                new ReserveChange(request.TransactionType, request.Amount, request.ChangeReason), user.UserId, now,
                command.CorrelationId, auditPosition: 0, token, replaces);
            replays.Record(claim.Id, user.UserId, now, key, result);

            await unitOfWork.SaveAsync(token);
            return result;
        }, cancellationToken);

        await ReservePostingReady.RequestAsync(publisher, user.OrganizationEntityId, command.ClaimId, transaction,
            cancellationToken);
        return transaction;
    }
}
