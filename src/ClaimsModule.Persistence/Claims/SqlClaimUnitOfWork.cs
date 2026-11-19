using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Claims;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Claims;

internal sealed class SqlClaimUnitOfWork(ClaimsDbContext db) : IClaimUnitOfWork
{
    public async Task<T> ExecuteIntakeAsync<T>(Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lockResource = $"ClaimsModule:FNOL:{db.OrganizationId:D}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @status int;
            EXEC @status = sys.sp_getapplock @Resource = {lockResource},
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @status < 0
                THROW 50001, 'Could not acquire the claim intake lock.', 1;
            """, cancellationToken);

        var result = await work(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

internal sealed class SqlClaimAuditLog(ClaimsDbContext db) : IClaimAuditLog
{
    public void Append(Claim claim, Guid correlationId, string eventType, string description, string? newValues,
        Guid? relatedEntityId = null) => db.Add(new ClaimAuditEntry(claim.OrganizationEntityId, claim.CreatedByUserId,
        claim.CreatedAt, claim.Id, correlationId, eventType, description, newValues, relatedEntityId));
}
