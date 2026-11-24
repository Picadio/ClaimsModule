using System.Text.Json;
using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Reserves;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Claims.Stores;

internal sealed class SqlClaimLifecycleStore(ClaimsDbContext db) : IClaimLifecycleStore
{
    public Task<Claim?> FindForUpdateAsync(Guid claimId, CancellationToken cancellationToken) =>
        db.Set<Claim>().Include(claim => claim.LossEvent)
            .Include(claim => claim.Parties)
            .Include(claim => claim.Warnings)
            .AsSplitQuery()
            .SingleOrDefaultAsync(claim => claim.Id == claimId, cancellationToken);

    public async Task<IReadOnlyList<ClaimTransitionRule>> ListTransitionsAsync(CancellationToken cancellationToken) =>
        await db.Set<StoredClaimTransition>().AsNoTracking()
            .Select(rule => new ClaimTransitionRule(rule.FromStatus, rule.ToStatus, rule.RequiredPermission))
            .ToListAsync(cancellationToken);

    public async Task<ClaimReserveFacts> ReadReserveFactsAsync(Guid claimId, CancellationToken cancellationToken)
    {
        var components = db.Set<ClaimReserveComponent>().Where(component => component.ClaimId == claimId);
        var history = db.Set<ReserveHistory>().Where(entry => entry.ClaimId == claimId &&
            components.Any(component => component.Id == entry.ReserveId));
        var approved = await history.AnyAsync(entry => entry.ApprovalStatus == ReserveApprovalStatus.Approved ||
            entry.ApprovalStatus == ReserveApprovalStatus.AutoApproved, cancellationToken);
        var pending = await history.AnyAsync(entry => entry.ApprovalStatus == ReserveApprovalStatus.PendingApproval,
            cancellationToken);
        var positive = await components.AnyAsync(component => component.CurrentAmount > 0, cancellationToken);
        return new ClaimReserveFacts(approved, pending, positive);
    }

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
