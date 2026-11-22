using ClaimsModule.Domain.Claims;

namespace ClaimsModule.Application.Claims;

public interface IClaimLifecycleStore
{
    Task<Claim?> FindForUpdateAsync(Guid claimId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ClaimTransitionRule>> ListTransitionsAsync(CancellationToken cancellationToken);
    Task<ClaimReserveFacts> ReadReserveFactsAsync(Guid claimId, CancellationToken cancellationToken);
    Task<StoredStatusChange?> FindStatusChangeAsync(Guid claimId, Guid userId, string keyHash,
        CancellationToken cancellationToken);
    void AddStatusChange(StoredStatusChange change);
}

public interface IClaimHandlerDirectory
{
    Task<bool> IsHandlerAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> FindByNameAsync(Guid organizationId, string name, CancellationToken cancellationToken);
}

public sealed record StoredStatusChange(Guid ActorId, DateTimeOffset OccurredAt, string KeyHash, string RequestHash,
    ClaimStatusResult Response);

public sealed record ClaimLifecycleOptions(bool AllowDirectClosure);
