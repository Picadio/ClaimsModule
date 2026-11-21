using System.Text.Json.Serialization;
using ClaimsModule.Application.Reserves;
using ClaimsModule.Domain.Claims;

namespace ClaimsModule.Application.Claims;

public sealed record CreateClaimRequest(Guid? PolicyId, string ClaimType, LossDetails LossEvent,
    IReadOnlyList<PartyDetails> Parties, IReadOnlyList<RiskDetails>? RiskObjects = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] InitialReserveRequest? InitialReserve = null);

public sealed record CreatedClaim(Guid Id, string ClaimNumber, ClaimStatus Status, IReadOnlyList<ClaimWarningDto> Warnings)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReserveTransaction? InitialReserve { get; init; }
}

public sealed record ClaimDetail(Guid Id, string ClaimNumber, ClaimStatus Status, string ClaimType, Guid? PolicyId,
    string? PolicyNumber, string? ClientName, DateTimeOffset ReportedAt, LossDetails LossEvent,
    IReadOnlyList<ClaimPartyDto> Parties, IReadOnlyList<ClaimRiskDto> RiskObjects, IReadOnlyList<ClaimWarningDto> Warnings)
{
    public Guid? AssignedHandlerId { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public string? ClosureReason { get; init; }
    public IReadOnlyList<ClaimAuditItem> RecentAuditEntries { get; init; } = [];
    public IReadOnlyList<ReserveBalance> ReserveBalances { get; init; } = [];
}

public sealed record ClaimPartyDto(Guid Id, ClaimPartyRole Role, ClaimPartyType PartyType, string? FirstName,
    string? LastName, string? CompanyName, string? Email, string? Phone, string? Notes, bool IsActive);

public sealed record ClaimRiskDto(Guid Id, RiskAssetType AssetType, string AssetDescription, string? DamageDescription,
    string? AssetReference, bool IsPrimary);

public sealed record ClaimWarningDto(string Code, string Field, string Message);
