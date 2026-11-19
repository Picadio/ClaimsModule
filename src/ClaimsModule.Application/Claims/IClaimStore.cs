using ClaimsModule.Domain.Claims;

namespace ClaimsModule.Application.Claims;

public interface IClaimStore
{
    Task<Claim?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<LinkedPolicy?> FindPolicyAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> IsCauseActiveAsync(string code, CancellationToken cancellationToken);
    Task<int> NextSequenceAsync(int year, CancellationToken cancellationToken);
    Task<StoredClaimSubmission?> FindSubmissionAsync(Guid userId, string keyHash, CancellationToken cancellationToken);
    void Add(Claim claim);
    void AddSubmission(StoredClaimSubmission submission);
}

public sealed record StoredClaimSubmission(Guid UserId, string KeyHash, string RequestHash, CreatedClaim Response);

public interface IClaimUnitOfWork
{
    Task<T> ExecuteIntakeAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
}

public interface IClaimAuditLog
{
    void Append(Claim claim, Guid correlationId, string eventType, string description, string? newValues,
        Guid? relatedEntityId = null);
}
