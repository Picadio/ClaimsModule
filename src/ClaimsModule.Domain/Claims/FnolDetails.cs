namespace ClaimsModule.Domain.Claims;

public sealed record FnolDetails(string ClaimType, LossDetails LossEvent, IReadOnlyList<PartyDetails> Parties,
    IReadOnlyList<RiskDetails> RiskObjects, ClaimSeverity Severity = ClaimSeverity.Standard);

public sealed record LossDetails(DateTimeOffset LossDate, string CauseOfLossCode, string LossDescription,
    string? LossLocation = null, decimal? EstimatedLossAmount = null, string? PoliceReportNumber = null);

public sealed record PartyDetails(ClaimPartyRole Role, ClaimPartyType PartyType, string? FirstName = null,
    string? LastName = null, string? CompanyName = null, string? Email = null, string? Phone = null,
    string? Notes = null);

public sealed record RiskDetails(RiskAssetType AssetType, string AssetDescription, string? DamageDescription = null,
    string? AssetReference = null, bool IsPrimary = false);

public sealed record LinkedPolicy(Guid Id, string PolicyNumber, string ClientName, DateOnly EffectiveDate,
    DateOnly ExpirationDate);

public enum ClaimStatus { Draft, Open, UnderInvestigation, PendingPayment, Closed, Reopened, Withdrawn }
public enum ClaimSeverity { Standard, Minor, Critical, Catastrophic }
public enum ClaimPartyRole { Claimant = 1, Insured, ThirdParty, Witness, Attorney }
public enum ClaimPartyType { Person = 1, Company }
public enum RiskAssetType { Vehicle = 1, Property, Person, Equipment, Other }
