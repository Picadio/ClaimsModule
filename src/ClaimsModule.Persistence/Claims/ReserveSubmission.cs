using ClaimsModule.Domain.Common;

namespace ClaimsModule.Persistence.Claims;

internal sealed class ReserveSubmission : OrganizationRecord
{
    public Guid ClaimId { get; private set; }
    public string KeyHash { get; private set; } = null!;
    public string RequestHash { get; private set; } = null!;
    public string ResponseJson { get; private set; } = null!;

    private ReserveSubmission()
    {
    }

    public ReserveSubmission(Guid organizationId, Guid actorId, DateTimeOffset occurredAt, Guid claimId, string keyHash,
        string requestHash, string responseJson) : base(organizationId, actorId, occurredAt)
    {
        ClaimId = claimId;
        KeyHash = keyHash;
        RequestHash = requestHash;
        ResponseJson = responseJson;
    }
}
