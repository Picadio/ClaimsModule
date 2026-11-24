using AutoMapper;
using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Claims;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Claims;

public sealed record AddClaimPartyCommand(Guid ClaimId, PartyDetails Request, string? IdempotencyKey, Guid CorrelationId)
    : IRequest<ClaimPartyDto?>;

public sealed record RemoveClaimPartyCommand(Guid ClaimId, Guid PartyId, string? IdempotencyKey, Guid CorrelationId)
    : IRequest<ClaimPartyDto?>;

internal sealed class AddClaimPartyValidator : AbstractValidator<AddClaimPartyCommand>
{
    public AddClaimPartyValidator()
    {
        RuleFor(command => command.Request).SetValidator(new PartyValidator());
        RuleFor(command => command.IdempotencyKey).OptionalKey();
    }
}

internal sealed class RemoveClaimPartyValidator : AbstractValidator<RemoveClaimPartyCommand>
{
    public RemoveClaimPartyValidator() => RuleFor(command => command.IdempotencyKey).OptionalKey();
}

internal sealed class ClaimPartyCommands(IClaimLifecycleStore store, IClaimUnitOfWork unitOfWork, ICurrentUser user,
    ClaimMutationReplays replays, IMapper mapper, IAuditLogService audit, TimeProvider clock)
    : IRequestHandler<AddClaimPartyCommand, ClaimPartyDto?>, IRequestHandler<RemoveClaimPartyCommand, ClaimPartyDto?>
{
    public Task<ClaimPartyDto?> Handle(AddClaimPartyCommand command, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteClaimWriteAsync<ClaimPartyDto?>(command.ClaimId, async token =>
        {
            var claim = await store.FindForUpdateAsync(command.ClaimId, token);
            if (claim is null)
            {
                return null;
            }

            var actor = user.Identity.UserId;
            var key = ClaimMutationKey.Create(command.IdempotencyKey, new { Action = "AddParty", command.Request });
            if (await replays.ReadAsync<ClaimPartyDto>(claim.Id, actor, key, token) is { } prior)
            {
                return prior;
            }

            var now = clock.GetUtcNow();
            var party = claim.AddParty(command.Request, actor, now);
            await unitOfWork.SaveAsync(token);
            audit.Append(ClaimAuditEvent.PartyAdded(claim, party, actor, now, command.CorrelationId));
            var result = mapper.Map<ClaimPartyDto>(party);
            replays.Record(claim.Id, actor, now, key, result);
            await unitOfWork.SaveAsync(token);
            return result;
        }, cancellationToken);

    public Task<ClaimPartyDto?> Handle(RemoveClaimPartyCommand command, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteClaimWriteAsync<ClaimPartyDto?>(command.ClaimId, async token =>
        {
            var claim = await store.FindForUpdateAsync(command.ClaimId, token);
            if (claim?.Parties.SingleOrDefault(party => party.Id == command.PartyId) is not { } party)
            {
                return null;
            }

            var actor = user.Identity.UserId;
            var key = ClaimMutationKey.Create(command.IdempotencyKey, new { Action = "RemoveParty", command.PartyId });
            if (await replays.ReadAsync<ClaimPartyDto>(claim.Id, actor, key, token) is { } prior)
            {
                return prior;
            }

            var now = clock.GetUtcNow();
            if (claim.DeactivateParty(party, actor, now))
            {
                await unitOfWork.SaveAsync(token);
                audit.Append(new ClaimAuditEvent(claim.OrganizationEntityId, claim.Id, actor, now, command.CorrelationId,
                    "PARTY_REMOVED", "A claim party was deactivated.", new { IsActive = true },
                    new { Role = party.Role.ToString(), party.IsActive }, party.Id, nameof(ClaimParty)));
            }

            var result = mapper.Map<ClaimPartyDto>(party);
            replays.Record(claim.Id, actor, now, key, result);
            await unitOfWork.SaveAsync(token);
            return result;
        }, cancellationToken);

}
