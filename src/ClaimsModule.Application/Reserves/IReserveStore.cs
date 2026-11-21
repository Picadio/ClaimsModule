using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Reserves;

namespace ClaimsModule.Application.Reserves;

public interface IReserveStore
{
    Task<Claim?> FindClaimForUpdateAsync(Guid claimId, CancellationToken cancellationToken);
    Task<ClaimReserveComponent?> FindComponentAsync(Guid claimId, Guid? reserveId, ReserveComponentType? component,
        CancellationToken cancellationToken);
    Task<ReserveHistory?> FindHistoryAsync(Guid claimId, Guid transactionId, CancellationToken cancellationToken);
    Task<bool> HasUncancelledRevisionAsync(Guid claimId, Guid rejectedTransactionId, CancellationToken cancellationToken);
    Task<bool> IsComponentArchivedAsync(Guid claimId, ReserveComponentType component, CancellationToken cancellationToken);
    Task<bool> HasPendingAddAsync(Guid claimId, Guid reserveId, CancellationToken cancellationToken);
    Task<decimal> SumApprovedAsync(Guid claimId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReserveBalance>> ReadBalancesAsync(Guid claimId, CancellationToken cancellationToken);
    Task<ReserveLedgerData?> ReadLedgerAsync(Guid claimId, CancellationToken cancellationToken);
    Task<StoredReserveSubmission?> FindSubmissionAsync(Guid claimId, Guid actorId, string keyHash,
        CancellationToken cancellationToken);
    void AddComponent(ClaimReserveComponent component);
    void AddHistory(ReserveHistory entry);
    void AddSubmission(StoredReserveSubmission submission);
}

public sealed record ReserveLedgerData(IReadOnlyList<ReserveBalance> Balances, IReadOnlyList<ReserveHistory> History,
    bool ReserveLimitOverrideEnabled);

public sealed record StoredReserveSubmission(Guid ClaimId, Guid ActorId, DateTimeOffset OccurredAt, string KeyHash,
    string RequestHash, string ResponseJson);
