using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Exceptions;

namespace ClaimsModule.Domain.Claims;

public sealed class Claim : OrganizationRecord
{
    private readonly List<ClaimParty> parties = [];
    private readonly List<ClaimRiskObject> riskObjects = [];
    private readonly List<ClaimWarning> warnings = [];

    public string ClaimNumber { get; private set; } = null!;
    public string ClaimType { get; private set; } = null!;
    public ClaimStatus Status { get; private set; }
    public Guid? PolicyId { get; private set; }
    public string? PolicyNumber { get; private set; }
    public string? ClientName { get; private set; }
    public LossEvent LossEvent { get; private set; } = null!;
    public IReadOnlyList<ClaimParty> Parties => parties.AsReadOnly();
    public IReadOnlyList<ClaimRiskObject> RiskObjects => riskObjects.AsReadOnly();
    public IReadOnlyList<ClaimWarning> Warnings => warnings.AsReadOnly();
    public ClaimCreated? CreatedEvent { get; }
    
    private Claim()
    {
    }

    private Claim(Guid organizationId, Guid actorId, DateTimeOffset reportedAt, int sequence, FnolDetails details,
        LinkedPolicy? policy) : base(organizationId, actorId, reportedAt)
    {
        ClaimNumber = FnolRules.FormatClaimNumber(reportedAt, sequence);
        ClaimType = details.ClaimType.Trim();
        Status = ClaimStatus.Draft;
        PolicyId = policy?.Id;
        PolicyNumber = policy?.PolicyNumber;
        ClientName = policy?.ClientName;
        LossEvent = new LossEvent(organizationId, actorId, reportedAt, details.LossEvent);
        parties.AddRange(details.Parties.Select(party => new ClaimParty(organizationId, actorId, reportedAt, party)));
        riskObjects.AddRange(details.RiskObjects.Select(risk => new ClaimRiskObject(organizationId, actorId, reportedAt, risk)));
        CreatedEvent = new ClaimCreated(CreatedAt);

        if (policy is null)
        {
            AddWarning("POLICY_UNKNOWN", "PolicyId", "The claim was reported without a known policy.");
        }
        else
        {
            var lossDate = DateOnly.FromDateTime(LossEvent.LossDate.UtcDateTime);
            if (lossDate < policy.EffectiveDate || lossDate > policy.ExpirationDate)
            {
                AddWarning("LOSS_OUTSIDE_POLICY_PERIOD", "LossEvent.LossDate",
                    "The loss date is outside the policy's effective period.");
            }
        }

        if (riskObjects.Count == 0)
        {
            AddWarning("RISK_OBJECTS_MISSING", "RiskObjects", "No risk objects were reported.");
        }
    }

    public static Claim Report(Guid organizationId, Guid actorId, DateTimeOffset reportedAt, int sequence,
        FnolDetails details, LinkedPolicy? policy, bool causeOfLossIsActive)
    {
        var loss = details.LossEvent;
        if (loss.LossDate == default || loss.LossDate > reportedAt)
        {
            throw new ClaimRuleViolation("LossEvent.LossDate", "The loss date is required and cannot be in the future.");
        }

        if (loss.LossDescription.Trim().Length < FnolRules.MinLossDescriptionLength)
        {
            throw new ClaimRuleViolation("LossEvent.LossDescription", "The loss description must contain at least 20 characters.");
        }

        if (!causeOfLossIsActive || string.IsNullOrWhiteSpace(loss.CauseOfLossCode))
        {
            throw new ClaimRuleViolation("LossEvent.CauseOfLossCode", "The cause of loss must be active for this organization.");
        }

        if (!details.Parties.Any(party => party.Role == ClaimPartyRole.Claimant))
        {
            throw new ClaimRuleViolation("Parties", "At least one claimant is required.");
        }

        return new Claim(organizationId, actorId, reportedAt, sequence, details, policy);
    }

    private void AddWarning(string code, string field, string message) =>
        warnings.Add(new ClaimWarning(OrganizationEntityId, CreatedByUserId, CreatedAt, code, field, message));
}

public sealed record ClaimCreated(DateTimeOffset OccurredAt);
