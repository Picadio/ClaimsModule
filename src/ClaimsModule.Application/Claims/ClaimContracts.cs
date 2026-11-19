using ClaimsModule.Domain.Claims;

namespace ClaimsModule.Application.Claims;

public sealed record CreateClaimRequest(Guid? PolicyId, string ClaimType, LossDetails LossEvent,
    IReadOnlyList<PartyDetails> Parties, IReadOnlyList<RiskDetails>? RiskObjects = null);

public sealed record CreatedClaim(Guid Id, string ClaimNumber, ClaimStatus Status, IReadOnlyList<ClaimWarningDto> Warnings);

public sealed record ClaimDetail(Guid Id, string ClaimNumber, ClaimStatus Status, string ClaimType, Guid? PolicyId,
    string? PolicyNumber, string? ClientName, DateTimeOffset ReportedAt, LossDetails LossEvent,
    IReadOnlyList<ClaimPartyDto> Parties, IReadOnlyList<ClaimRiskDto> RiskObjects, IReadOnlyList<ClaimWarningDto> Warnings);

public sealed record ClaimPartyDto(Guid Id, ClaimPartyRole Role, ClaimPartyType PartyType, string? FirstName,
    string? LastName, string? CompanyName, string? Email, string? Phone, string? Notes, bool IsActive);

public sealed record ClaimRiskDto(Guid Id, RiskAssetType AssetType, string AssetDescription, string? DamageDescription,
    string? AssetReference, bool IsPrimary);

public sealed record ClaimWarningDto(string Code, string Field, string Message);
