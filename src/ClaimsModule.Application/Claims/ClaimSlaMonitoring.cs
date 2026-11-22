using MediatR;

namespace ClaimsModule.Application.Claims;

public sealed record ListSlaCandidatesQuery(DateTimeOffset Now, Guid? AfterId, int PageSize)
    : IRequest<IReadOnlyList<Guid>>;

public sealed record DetectClaimSlaBreachCommand(Guid ClaimId, DateTimeOffset Now, Guid CorrelationId) : IRequest<bool>;

internal sealed class ListSlaCandidatesHandler(IClaimStore store)
    : IRequestHandler<ListSlaCandidatesQuery, IReadOnlyList<Guid>>
{
    public Task<IReadOnlyList<Guid>> Handle(ListSlaCandidatesQuery query, CancellationToken cancellationToken) =>
        store.ListSlaCandidatesAsync(query.Now, query.AfterId, query.PageSize, cancellationToken);
}

internal sealed class DetectClaimSlaBreachHandler(IClaimStore store, IClaimUnitOfWork unitOfWork, IAuditLogService audit)
    : IRequestHandler<DetectClaimSlaBreachCommand, bool>
{
    public Task<bool> Handle(DetectClaimSlaBreachCommand command, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteClaimJobAsync(command.ClaimId, async token =>
        {
            var claim = await store.FindForWriteAsync(command.ClaimId, token);
            if (claim is null || !claim.TryRecordSlaBreach(command.Now))
            {
                return false;
            }

            await unitOfWork.SaveAsync(token);
            audit.Append(new ClaimAuditEvent(claim.OrganizationEntityId, claim.Id, null, command.Now,
                command.CorrelationId, "SLA_BREACH_DETECTED", "Claim has not been updated in 48 hours.",
                NewValues: new { claim.LastSlaBreachAt, LastActivityAt = claim.UpdatedAt ?? claim.CreatedAt }));
            await unitOfWork.SaveAsync(token);
            return true;
        }, cancellationToken);
}
