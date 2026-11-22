using System.Data;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Claims;

internal sealed class SqlClaimUnitOfWork(ClaimsDbContext db) : IClaimUnitOfWork
{
    public Task<T> ExecuteIntakeAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken) =>
        ExecuteAsync($"ClaimsModule:FNOL:{db.OrganizationId:D}", work, cancellationToken);

    public Task<T> ExecuteClaimWriteAsync<T>(Guid claimId, Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken) =>
        ExecuteAsync($"ClaimsModule:Claim:{db.OrganizationId:D}:{claimId:D}", work, cancellationToken);

    public Task<T> ExecuteClaimJobAsync<T>(Guid claimId, Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken) =>
        ExecuteAsync($"ClaimsModule:Claim:{db.OrganizationId:D}:{claimId:D}", work, cancellationToken, IsolationLevel.RepeatableRead);

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ClaimConcurrencyException(exception);
        }
    }

    private async Task<T> ExecuteAsync<T>(string lockResource, Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken, IsolationLevel isolation = IsolationLevel.ReadCommitted)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(isolation, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @status int;
            EXEC @status = sys.sp_getapplock @Resource = {lockResource},
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @status < 0
                THROW 50001, 'Could not acquire the claim write lock.', 1;
            """, cancellationToken);

        var result = await work(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
