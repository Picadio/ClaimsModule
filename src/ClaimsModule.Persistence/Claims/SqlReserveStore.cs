using System.Data;
using System.Text.Json;
using ClaimsModule.Application.Reserves;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Reserves;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Claims;

internal sealed class SqlReserveStore(ClaimsDbContext db) : IReserveStore
{
    public Task<Claim?> FindClaimForUpdateAsync(Guid claimId, CancellationToken cancellationToken) =>
        db.Set<Claim>().SingleOrDefaultAsync(claim => claim.Id == claimId, cancellationToken);

    public Task<ClaimReserveComponent?> FindComponentAsync(Guid claimId, Guid? reserveId, ReserveComponentType? component,
        CancellationToken cancellationToken)
    {
        var components = db.Set<ClaimReserveComponent>().Where(row => row.ClaimId == claimId);
        return reserveId is Guid id
            ? components.SingleOrDefaultAsync(row => row.Id == id, cancellationToken)
            : components.SingleOrDefaultAsync(row => row.ComponentType == component, cancellationToken);
    }

    public Task<bool> IsComponentArchivedAsync(Guid claimId, ReserveComponentType component,
        CancellationToken cancellationToken) =>
        db.Set<ClaimReserveComponent>().IgnoreQueryFilters()
            .AnyAsync(row => row.OrganizationEntityId == db.OrganizationId && row.ClaimId == claimId &&
                row.ComponentType == component && row.IsDeleted, cancellationToken);

    public Task<bool> HasPendingAddAsync(Guid claimId, Guid reserveId, CancellationToken cancellationToken) =>
        db.Set<ReserveHistory>().AnyAsync(entry => entry.ClaimId == claimId && entry.ReserveId == reserveId &&
            entry.TransactionType == ReserveTransactionType.Add && entry.ApprovalStatus == ReserveApprovalStatus.PendingApproval,
            cancellationToken);

    public Task<decimal> SumApprovedAsync(Guid claimId, CancellationToken cancellationToken) =>
        db.Set<ClaimReserveComponent>().Where(component => component.ClaimId == claimId)
            .SumAsync(component => component.CurrentAmount, cancellationToken);

    public async Task<IReadOnlyList<ReserveBalance>> ReadBalancesAsync(Guid claimId, CancellationToken cancellationToken) =>
        await db.Set<ClaimReserveComponent>()
            .Where(component => component.ClaimId == claimId && db.Set<Claim>().Any(claim => claim.Id == component.ClaimId))
            .OrderBy(component => component.ComponentType)
            .Select(component => new ReserveBalance(component.Id, component.ComponentType, component.CurrentAmount,
                db.Set<ReserveHistory>().Where(entry => entry.ClaimId == claimId && entry.ReserveId == component.Id &&
                    entry.ApprovalStatus == ReserveApprovalStatus.PendingApproval).Sum(entry => entry.Amount)))
            .ToListAsync(cancellationToken);

    public async Task<ReserveLedgerData?> ReadLedgerAsync(Guid claimId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await db.Set<Claim>().AnyAsync(claim => claim.Id == claimId, cancellationToken))
        {
            return null;
        }

        var balances = await ReadBalancesAsync(claimId, cancellationToken);
        var history = await db.Set<ReserveHistory>().AsNoTracking()
            .Where(entry => entry.ClaimId == claimId && db.Set<ClaimReserveComponent>()
                .Any(component => component.Id == entry.ReserveId && component.ClaimId == claimId))
            .OrderByDescending(entry => entry.ChangeSequence)
            .ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ReserveLedgerData(balances, history);
    }

    public async Task<StoredReserveSubmission?> FindSubmissionAsync(Guid claimId, Guid actorId, string keyHash,
        CancellationToken cancellationToken)
    {
        var record = await db.Set<ReserveSubmission>().AsNoTracking().SingleOrDefaultAsync(row => row.ClaimId == claimId &&
            row.CreatedByUserId == actorId && row.KeyHash == keyHash, cancellationToken);
        return record is null ? null : new StoredReserveSubmission(claimId, actorId, record.CreatedAt, keyHash,
            record.RequestHash, JsonSerializer.Deserialize<ReserveTransaction>(record.ResponseJson)!);
    }

    public void AddComponent(ClaimReserveComponent component) => db.Add(component);

    public void AddHistory(ReserveHistory entry) => db.Add(entry);

    public void AddSubmission(StoredReserveSubmission submission) => db.Add(new ReserveSubmission(db.OrganizationId,
        submission.ActorId, submission.OccurredAt, submission.ClaimId, submission.KeyHash, submission.RequestHash,
        JsonSerializer.Serialize(submission.Response)));
}
