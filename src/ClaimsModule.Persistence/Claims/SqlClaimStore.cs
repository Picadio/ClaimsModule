using System.Text.Json;
using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Claims;
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
