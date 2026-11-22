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
    public ClaimSeverity Severity { get; private set; }
    public string? Notes { get; private set; }
    public Guid? PolicyId { get; private set; }
    public string? PolicyNumber { get; private set; }
    public string? ClientName { get; private set; }
    public Guid? AssignedHandlerId { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public string? ClosureReason { get; private set; }
    public bool ReserveLimitOverrideEnabled { get; private set; }
    public DateTimeOffset? LastSlaBreachAt { get; private set; }
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
        Severity = details.Severity;
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
            AddWarning(ClaimWarning.UnknownPolicy, "PolicyId", "The claim was reported without a known policy.");
        }
        else
        {
            if (FnolRules.IsOutsidePolicyPeriod(LossEvent.LossDate, policy))
            {
                AddPolicyPeriodWarning(actorId, reportedAt);
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
        var issue = GetLossIssues(loss.LossDate, loss.LossDescription, loss.CauseOfLossCode, causeOfLossIsActive,
            reportedAt).FirstOrDefault();
        if (issue is not null)
        {
            throw new ClaimRuleViolation(issue.Field, issue.Message);
        }

        if (!details.Parties.Any(party => party.Role == ClaimPartyRole.Claimant))
        {
            throw new ClaimRuleViolation("Parties", "At least one claimant is required.");
        }

        return new Claim(organizationId, actorId, reportedAt, sequence, details, policy);
    }

    public ClaimStatusChange ChangeStatus(ClaimStatus target, ClaimTransitionDetails details,
        ClaimLifecycleFacts facts, Guid actorId, DateTimeOffset occurredAt)
    {
        var previousStatus = Status;
        var previousHandlerId = AssignedHandlerId;
        var opensDraft = Status == ClaimStatus.Draft && target == ClaimStatus.Open;
        var reachesOpen = target is ClaimStatus.Open or ClaimStatus.Reopened;
        var reason = string.IsNullOrWhiteSpace(details.Reason) ? null : details.Reason.Trim();
        var handlerId = details.AssignedHandlerId ?? AssignedHandlerId;
        var policyWarning = opensDraft
            ? warnings.FirstOrDefault(warning => warning.Code == ClaimWarning.OutsidePolicyPeriod)
            : null;

        if (Status == target || (Status == ClaimStatus.Draft && target == ClaimStatus.Closed))
        {
            throw new ClaimRuleViolation("TargetStatus", $"The transition from {Status} to {target} is not allowed.");
        }

        var issues = new List<ClaimRuleError>();
        if (details.AssignedHandlerId.HasValue && !opensDraft)
        {
            issues.Add(new("AssignedHandlerId", "A handler can be assigned when opening a draft."));
        }

        if (reachesOpen || target == ClaimStatus.Closed)
        {
            issues.AddRange(GetCriticalIssues(facts.CauseOfLossIsActive, occurredAt));
        }

        if (reachesOpen && (handlerId is null || handlerId == Guid.Empty))
        {
            issues.Add(new("AssignedHandlerId", "Assign a handler before opening the claim."));
        }

        if (policyWarning is not null && !details.AcknowledgePolicyPeriodWarning)
        {
            issues.Add(new("AcknowledgePolicyPeriodWarning", "Acknowledge the loss date warning before opening the claim."));
        }

        if (target == ClaimStatus.PendingPayment)
        {
            if (!facts.Reserves.HasApprovedReserve)
            {
                issues.Add(new("TargetStatus", "At least one approved reserve is required."));
            }

            if (Status == ClaimStatus.UnderInvestigation && !details.LiabilityDetermined)
            {
                issues.Add(new("LiabilityDetermined", "Determine liability before moving to PendingPayment."));
            }
        }

        if (target == ClaimStatus.Closed)
        {
            if (facts.Reserves.HasPendingApproval)
            {
                issues.Add(new("TargetStatus", "Resolve pending reserve approvals before closing the claim."));
            }

            if (facts.Reserves.HasPositiveComponentBalance && (!details.ConfirmPositiveReserveBalance || reason is null))
            {
                issues.Add(new("ConfirmPositiveReserveBalance",
                    "Confirm the positive reserve balance and enter a reason before closing the claim."));
            }
        }

        if (target is ClaimStatus.Reopened or ClaimStatus.Withdrawn && reason is null)
        {
            issues.Add(new("Reason", $"A reason is required for {target}."));
        }

        if (issues.Count > 0)
        {
            throw new ClaimRuleViolation(issues);
        }

        Status = target == ClaimStatus.Reopened ? ClaimStatus.Open : target;
        AssignedHandlerId = handlerId;
        ClosedAt = target == ClaimStatus.Closed ? occurredAt.ToUniversalTime() : null;
        ClosureReason = target == ClaimStatus.Closed ? reason : null;
        MarkUpdated(actorId, occurredAt);

        return new ClaimStatusChange(previousStatus, target, Status, previousHandlerId, reason, policyWarning,
            target == ClaimStatus.Closed && facts.Reserves.HasPositiveComponentBalance,
            target == ClaimStatus.PendingPayment && previousStatus == ClaimStatus.UnderInvestigation);
    }

    public void EnsureCanSubmitReserve()
    {
        if (PolicyId is null)
        {
            throw new ClaimRuleViolation("PolicyId", "Link a policy before submitting a reserve.");
        }

        if (Status is ClaimStatus.Closed or ClaimStatus.Withdrawn)
        {
            throw new ClaimRuleViolation("Status", "Reserves cannot be submitted on a closed or withdrawn claim.");
        }
    }

    public void RecordActivity(Guid actorId, DateTimeOffset occurredAt) => MarkUpdated(actorId, occurredAt);

    public bool EnableReserveLimitOverride(Guid actorId, DateTimeOffset occurredAt)
    {
        EnsureCanSubmitReserve();
        if (ReserveLimitOverrideEnabled)
        {
            return false;
        }

        ReserveLimitOverrideEnabled = true;
        MarkUpdated(actorId, occurredAt);
        return true;
    }

    public bool TryRecordSlaBreach(DateTimeOffset now)
    {
        if (Status is not (ClaimStatus.Draft or ClaimStatus.Open) ||
            (UpdatedAt ?? CreatedAt) >= now.AddHours(-ClaimSla.InactivityHours) ||
            LastSlaBreachAt > now.AddHours(-ClaimSla.RepeatHours))
        {
            return false;
        }

        LastSlaBreachAt = now.ToUniversalTime();
        return true;
    }

    public bool IsSlaBreached(DateTimeOffset now) =>
        Status is ClaimStatus.Draft or ClaimStatus.Open &&
        (UpdatedAt ?? CreatedAt) < now.AddHours(-ClaimSla.InactivityHours) &&
        LastSlaBreachAt >= (UpdatedAt ?? CreatedAt);

    public ClaimParty AddParty(PartyDetails details, Guid actorId, DateTimeOffset now)
    {
        var party = new ClaimParty(OrganizationEntityId, actorId, now, details);
        parties.Add(party);
        MarkUpdated(actorId, now);
        return party;
    }

    public bool DeactivateParty(ClaimParty party, Guid actorId, DateTimeOffset now)
    {
        if (!parties.Contains(party) || party.IsDeleted)
        {
            throw new ClaimRuleViolation("PartyId", "The party does not belong to this claim.");
        }

        if (!party.IsActive)
        {
            return false;
        }

        if (party.Role == ClaimPartyRole.Claimant &&
            !parties.Any(other => other != party && other.Role == ClaimPartyRole.Claimant &&
                other.IsActive && !other.IsDeleted))
        {
            throw new ClaimRuleViolation("PartyId", "The last active claimant cannot be removed.");
        }

        party.Deactivate(actorId, now);
        MarkUpdated(actorId, now);
        return true;
    }

    public bool SetNotes(string? notes, Guid actorId, DateTimeOffset now)
    {
        var normalized = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (Notes == normalized)
        {
            return false;
        }

        Notes = normalized;
        MarkUpdated(actorId, now);
        return true;
    }

    public ClaimPolicyChange? LinkPolicy(LinkedPolicy policy, bool acknowledgePolicyPeriodWarning, Guid actorId, DateTimeOffset now)
    {
        if (PolicyId == policy.Id)
        {
            return null;
        }

        if (PolicyId.HasValue || Status is ClaimStatus.Closed or ClaimStatus.Withdrawn)
        {
            throw new ClaimRuleViolation("PolicyId", "Only an unresolved policy on a nonterminal claim can be linked.");
        }

        var outsidePeriod = FnolRules.IsOutsidePolicyPeriod(LossEvent.LossDate, policy);
        if (outsidePeriod && Status != ClaimStatus.Draft && !acknowledgePolicyPeriodWarning)
        {
            throw new ClaimRuleViolation("AcknowledgePolicyPeriodWarning",
                "Acknowledge the loss date warning before linking this policy.");
        }

        PolicyId = policy.Id;
        PolicyNumber = policy.PolicyNumber;
        ClientName = policy.ClientName;
        var resolved = warnings.FirstOrDefault(warning => warning.Code == ClaimWarning.UnknownPolicy && !warning.IsDeleted);
        resolved?.Resolve(actorId, now);
        var outsideWarning = outsidePeriod ? AddPolicyPeriodWarning(actorId, now) : null;
        MarkUpdated(actorId, now);
        return new ClaimPolicyChange(resolved, outsideWarning, outsidePeriod && Status != ClaimStatus.Draft);
    }

    private IEnumerable<ClaimRuleError> GetCriticalIssues(bool causeOfLossIsActive, DateTimeOffset occurredAt)
    {
        if (LossEvent is null)
        {
            yield return new("LossEvent", "The claim must have a loss event.");
        }
        else
        {
            foreach (var issue in GetLossIssues(LossEvent.LossDate, LossEvent.LossDescription, LossEvent.CauseOfLossCode,
                causeOfLossIsActive, occurredAt))
            {
                yield return issue;
            }
        }
        if (!parties.Any(party => party.Role == ClaimPartyRole.Claimant && party.IsActive && !party.IsDeleted))
        {
            yield return new("Parties", "At least one active claimant is required.");
        }
    }

    private static IEnumerable<ClaimRuleError> GetLossIssues(DateTimeOffset lossDate, string? description, string causeOfLossCode,
        bool causeOfLossIsActive, DateTimeOffset now)
    {
        if (lossDate == default || lossDate > now)
        {
            yield return new("LossEvent.LossDate", "The loss date is required and cannot be in the future.");
        }

        if (description is null || description.Trim().Length < FnolRules.MinLossDescriptionLength)
        {
            yield return new("LossEvent.LossDescription", "The loss description must contain at least 20 characters.");
        }

        if (!causeOfLossIsActive || string.IsNullOrWhiteSpace(causeOfLossCode))
        {
            yield return new("LossEvent.CauseOfLossCode", "The cause of loss must be active for this organization.");
        }
    }

    private void AddWarning(string code, string field, string message) =>
        warnings.Add(new ClaimWarning(OrganizationEntityId, CreatedByUserId, CreatedAt, code, field, message));

    private ClaimWarning AddPolicyPeriodWarning(Guid actorId, DateTimeOffset now)
    {
        var warning = new ClaimWarning(OrganizationEntityId, actorId, now, ClaimWarning.OutsidePolicyPeriod,
            "LossEvent.LossDate", "The loss date is outside the policy's effective period.");
        warnings.Add(warning);
        return warning;
    }
}

public sealed record ClaimCreated(DateTimeOffset OccurredAt);
public sealed record ClaimPolicyChange(ClaimWarning? ResolvedWarning, ClaimWarning? AddedWarning, bool Acknowledged);

public static class ClaimSla
{
    public const int InactivityHours = 48;
    public const int RepeatHours = 24;
}
