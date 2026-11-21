using System.Text.Json;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Exceptions;

namespace ClaimsModule.Application.Reserves;

internal readonly record struct ReserveReplayKey(string? KeyHash, string? RequestHash)
{
    public static ReserveReplayKey Create<T>(string? key, T request) => key is null
        ? default : new(IdempotencyFingerprint.HashKey(key), IdempotencyFingerprint.HashRequest(request));
}

internal sealed class ReserveReplays(IReserveStore store)
{
    public async Task<T?> ReadAsync<T>(Guid claimId, Guid actorId, ReserveReplayKey key, CancellationToken cancellationToken)
        where T : class
    {
        if (key.KeyHash is null ||
            await store.FindSubmissionAsync(claimId, actorId, key.KeyHash, cancellationToken) is not { } prior)
        {
            return null;
        }

        if (prior.RequestHash != key.RequestHash)
        {
            throw new IdempotencyConflictException();
        }

        return JsonSerializer.Deserialize<T>(prior.ResponseJson)!;
    }

    public void Record<T>(Guid claimId, Guid actorId, DateTimeOffset occurredAt, ReserveReplayKey key, T response)
    {
        if (key.KeyHash is not null)
        {
            store.AddSubmission(new StoredReserveSubmission(claimId, actorId, occurredAt, key.KeyHash,
                key.RequestHash!, JsonSerializer.Serialize(response)));
        }
    }
}
