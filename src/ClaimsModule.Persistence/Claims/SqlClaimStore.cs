using System.Text.Json;
using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Reserves;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Claims;

internal sealed class SqlClaimStore(ClaimsDbContext db, TimeProvider clock) : IClaimStore
{
    public Task<Claim?> FindAsync(Guid id, CancellationToken cancellationToken) => db.Set<Claim>()
        .AsNoTracking()
        .Include(row => row.LossEvent)
        .Include(row => row.Parties)
        .Include(row => row.RiskObjects)
        .Include(row => row.Warnings)
        .AsSplitQuery()
        .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);

    public async Task<ClaimPage> ListAsync(ListClaimsQuery request, CancellationToken cancellationToken)
    {
        var claims = db.Set<Claim>().AsNoTracking();

        if (request.Status is { Length: > 0 } statuses)
        {
            claims = claims.Where(row => statuses.Contains(row.Status));
        }

        if (request.DateFrom is { } dateFrom)
        {
            DateTimeOffset start = dateFrom.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            claims = claims.Where(row => row.LossEvent.LossDate >= start);
        }

        if (request.DateTo is { } dateTo && dateTo < DateOnly.MaxValue)
        {
            DateTimeOffset end = dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            claims = claims.Where(row => row.LossEvent.LossDate < end);
        }

        if (request.AssignedHandlerId is { } handlerId)
        {
            claims = claims.Where(row => row.AssignedHandlerId == handlerId);
        }

        if (request.PolicyId is { } policyId)
        {
            claims = claims.Where(row => row.PolicyId == policyId);
        }

        if (request.CauseOfLossCode?.Trim().ToUpperInvariant() is { Length: > 0 } cause)
        {
            claims = claims.Where(row => row.LossEvent.CauseOfLossCode == cause);
        }

        if (request.Search?.Trim() is { Length: > 0 } search)
        {
            claims = claims.Where(row => row.ClaimNumber.Contains(search) ||
                (row.ClientName != null && row.ClientName.Contains(search)));
        }

        var totalCount = await claims.CountAsync(cancellationToken);
        var rows = await claims
            .OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(row => new ClaimSummary(row.Id, row.ClaimNumber, row.ClientName, row.PolicyNumber,
                row.LossEvent.LossDate, row.LossEvent.CauseOfLossCode, row.Status,
                db.Set<ClaimReserveComponent>().Where(component => component.ClaimId == row.Id)
                    .Sum(component => component.CurrentAmount), row.AssignedHandlerId))
            .ToListAsync(cancellationToken);

        return new ClaimPage(rows, request.Page, request.PageSize, totalCount);
    }

    public Task<LinkedPolicy?> FindPolicyAsync(Guid id, CancellationToken cancellationToken) => db.Set<Policy>()
        .Where(row => row.Id == id)
        .Select(row => new LinkedPolicy(row.Id, row.PolicyNumber, row.ClientName, row.EffectiveDate, row.ExpirationDate))
        .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> IsCauseActiveAsync(string code, CancellationToken cancellationToken) => db.Set<CauseOfLoss>()
        .AnyAsync(row => row.Code == code && row.IsActive, cancellationToken);

    public async Task<int> NextSequenceAsync(int year, CancellationToken cancellationToken)
    {
        var counters = db.Set<ClaimNumberCounter>();
        var counter = await counters.SingleOrDefaultAsync(row => row.Year == year, cancellationToken);
        if (counter is null)
        {
            counter = new ClaimNumberCounter(db.OrganizationId, year, clock.GetUtcNow());
            counters.Add(counter);
        }

        return counter.Advance();
    }

    public async Task<StoredClaimSubmission?> FindSubmissionAsync(Guid userId, string keyHash,
        CancellationToken cancellationToken)
    {
        var prior = await db.Set<ClaimSubmission>().AsNoTracking().SingleOrDefaultAsync(
            row => row.CreatedByUserId == userId && row.KeyHash == keyHash, cancellationToken);
        return prior is null ? null : new StoredClaimSubmission(userId, keyHash, prior.RequestHash,
            JsonSerializer.Deserialize<CreatedClaim>(prior.ResponseJson)!);
    }

    public void Add(Claim claim) => db.Add(claim);

    public void AddSubmission(StoredClaimSubmission submission) => db.Add(new ClaimSubmission(db.OrganizationId,
        submission.UserId, clock.GetUtcNow(), submission.Response.Id, submission.KeyHash, submission.RequestHash,
        JsonSerializer.Serialize(submission.Response)));
}
