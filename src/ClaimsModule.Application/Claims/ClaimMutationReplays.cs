using System.Text.Json;
using ClaimsModule.Application.Exceptions;
using FluentValidation;

namespace ClaimsModule.Application.Claims;

public interface IClaimMutationStore
{
    Task<StoredClaimMutation?> FindAsync(Guid claimId, Guid actorId, string keyHash, CancellationToken cancellationToken);
    void Add(StoredClaimMutation mutation);
}

public sealed record StoredClaimMutation(Guid ClaimId, Guid ActorId, DateTimeOffset OccurredAt, string KeyHash,
    string RequestHash, string ResponseJson);

internal readonly record struct ClaimMutationKey(string? KeyHash, string? RequestHash)
{
    public static ClaimMutationKey Create<T>(string? key, T request) => key is null
        ? default : new(IdempotencyFingerprint.HashKey(key), IdempotencyFingerprint.HashRequest(request));
}

internal sealed class ClaimMutationReplays(IClaimMutationStore store)
{
    public async Task<T?> ReadAsync<T>(Guid claimId, Guid actorId, ClaimMutationKey key, CancellationToken cancellationToken)
        where T : class
    {
        if (key.KeyHash is null ||
            await store.FindAsync(claimId, actorId, key.KeyHash, cancellationToken) is not { } existing)
        {
            return null;
        }

        if (existing.RequestHash != key.RequestHash)
        {
            throw new IdempotencyConflictException();
        }

        return JsonSerializer.Deserialize<T>(existing.ResponseJson)!;
    }

    public void Record<T>(Guid claimId, Guid actorId, DateTimeOffset utc, ClaimMutationKey key, T result)
    {
        if (key.KeyHash is not null)
        {
            store.Add(new StoredClaimMutation(claimId, actorId, utc, key.KeyHash, key.RequestHash!,
                JsonSerializer.Serialize(result)));
        }
    }
}

internal static class ClaimMutationValidation
{
    public static void OptionalKey<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(255).Must(key => key is null || !string.IsNullOrWhiteSpace(key))
            .WithMessage("Idempotency-Key cannot be blank.");
}
