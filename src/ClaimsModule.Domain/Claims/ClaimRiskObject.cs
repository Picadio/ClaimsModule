using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Claims;

public sealed class ClaimRiskObject : OrganizationRecord
{
    public Guid ClaimId { get; private set; }
    public RiskAssetType AssetType { get; private set; }
    public string AssetDescription { get; private set; } = null!;
    public string? DamageDescription { get; private set; }
    public string? AssetReference { get; private set; }
    public bool IsPrimary { get; private set; }
    
    private ClaimRiskObject()
    {
    }

    internal ClaimRiskObject(Guid organizationId, Guid actorId, DateTimeOffset reportedAt, RiskDetails details)
        : base(organizationId, actorId, reportedAt)
    {
        AssetType = details.AssetType;
        AssetDescription = details.AssetDescription.Trim();
        DamageDescription = details.DamageDescription?.Trim();
        AssetReference = details.AssetReference?.Trim();
        IsPrimary = details.IsPrimary;
    }
}
