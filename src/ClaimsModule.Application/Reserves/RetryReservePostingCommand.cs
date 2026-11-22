using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Reserves;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Reserves;

public sealed record RetryReservePostingCommand(Guid ClaimId, Guid TransactionId, string? IdempotencyKey,
    Guid CorrelationId) : IRequest<ReserveTransaction?>;

internal sealed class RetryReservePostingValidator : AbstractValidator<RetryReservePostingCommand>
{
    public RetryReservePostingValidator()
    {
        RuleFor(command => command.IdempotencyKey).MaximumLength(255)
            .Must(key => key is null || !string.IsNullOrWhiteSpace(key))
            .WithMessage("Idempotency-Key cannot be blank.");
    }
}

internal sealed class RetryReservePostingHandler(IReserveStore reserves, IClaimUnitOfWork unitOfWork, ICurrentUser currentUser,
    ReserveReplays replays, IAuditLogService audit, IPublisher publisher, TimeProvider clock)
    : IRequestHandler<RetryReservePostingCommand, ReserveTransaction?>
{
    public async Task<ReserveTransaction?> Handle(RetryReservePostingCommand command, CancellationToken cancellationToken)
    {
        var user = currentUser.Identity;
        var transaction = await unitOfWork.ExecuteClaimJobAsync<ReserveTransaction?>(command.ClaimId, async token =>
        {
            var claim = await reserves.FindClaimForUpdateAsync(command.ClaimId, token);
            if (claim is null ||
                await reserves.FindHistoryAsync(claim.Id, command.TransactionId, token) is not { } entry ||
                await reserves.FindComponentAsync(claim.Id, entry.ReserveId, null, token) is not { } component)
            {
                return null;
            }

            var key = ReserveReplayKey.Create(command.IdempotencyKey, new { Action = "RetryPosting", command.TransactionId });
            if (await replays.ReadAsync<ReserveTransaction>(claim.Id, user.UserId, key, token) is { } prior)
            {
                return prior;
            }

            var previousJobId = entry.PostingJobId;
            entry.RetryPosting(command.CorrelationId);
            await unitOfWork.SaveAsync(token);

            var result = ReserveTransaction.From(entry, component.ComponentType);
            var now = clock.GetUtcNow();
            audit.Append(new ClaimAuditEvent(claim.OrganizationEntityId, claim.Id, user.UserId, now,
                command.CorrelationId, "GL_POSTING_RETRY_REQUESTED", "A retry was requested for the reserve GL posting.",
                OldValues: new { PostingJobId = previousJobId }, NewValues: new { entry.PostingKey },
                RelatedEntityId: entry.Id, RelatedEntityType: nameof(ReserveHistory)));
            replays.Record(claim.Id, user.UserId, now, key, result);
            await unitOfWork.SaveAsync(token);
            return result;
        }, cancellationToken);

        await ReservePostingReady.RequestAsync(publisher, user.OrganizationEntityId, command.ClaimId, transaction,
            cancellationToken);
        return transaction;
    }
}
