using System.Text.Json;
using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Claims;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Claims;

internal sealed class SqlClaimLifecycleStore(ClaimsDbContext db) : IClaimLifecycleStore
{
    public Task<Claim?> FindForUpdateAsync(Guid claimId, CancellationToken cancellationToken) =>
        db.Set<Claim>().Include(claim => claim.LossEvent)
            .Include(claim => claim.Parties)
            .Include(claim => claim.Warnings)
            .AsSplitQuery()
            .SingleOrDefaultAsync(claim => claim.Id == claimId, cancellationToken);

    public async Task<IReadOnlyList<ClaimTransitionRule>> ListTransitionsAsync(CancellationToken cancellationToken) =>
        await db.Set<ClaimTransitionRule>().AsNoTracking().ToListAsync(cancellationToken);

    // The reserve module will read its persisted facts inside the same claim write transaction.
    public Task<ClaimReserveFacts> ReadReserveFactsAsync(Guid claimId, CancellationToken cancellationToken) =>
        Task.FromResult(default(ClaimReserveFacts));

    public async Task<StoredStatusChange?> FindStatusChangeAsync(Guid claimId, Guid userId, string keyHash,
        CancellationToken cancellationToken)
    {
        var prior = await db.Set<ClaimStatusSubmission>().AsNoTracking().SingleOrDefaultAsync(
            row => row.ClaimId == claimId && row.CreatedByUserId == userId && row.KeyHash == keyHash, cancellationToken);
        return prior is null ? null : new StoredStatusChange(userId, prior.CreatedAt, keyHash, prior.RequestHash,
            JsonSerializer.Deserialize<ClaimStatusResult>(prior.ResponseJson)!);
    }

    public void AddStatusChange(StoredStatusChange change) => db.Add(new ClaimStatusSubmission(db.OrganizationId,
        change.ActorId, change.OccurredAt, change.Response.Id, change.KeyHash, change.RequestHash,
        JsonSerializer.Serialize(change.Response)));
}
