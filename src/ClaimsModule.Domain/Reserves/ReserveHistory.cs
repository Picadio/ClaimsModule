using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Exceptions;

namespace ClaimsModule.Domain.Reserves;

public sealed class ReserveHistory : OrganizationRecord
{
    public Guid ClaimId { get; private set; }
    public Guid ReserveId { get; private set; }
    public int ChangeSequence { get; private set; }
    public ReserveTransactionType TransactionType { get; private set; }
    public decimal Amount { get; private set; }
    public decimal PreviousBalance { get; private set; }
    public decimal NewBalance { get; private set; }
    public string ChangeReason { get; private set; } = null!;
    public ReserveApprovalAuthority RequiredAuthority { get; private set; }
    public bool RequiresManagerOverride { get; private set; }
    public ReserveApprovalStatus ApprovalStatus { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public Guid? RejectedByUserId { get; private set; }
    public DateTimeOffset? RejectedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public Guid? ReplacesRejectedTransactionId { get; private set; }
    public ReservePostingStatus PostingStatus { get; private set; }
    public string PostingKey { get; private set; } = null!;
    public string? PostingJobId { get; private set; }

    private ReserveHistory()
    {
    }

    internal ReserveHistory(ClaimReserveComponent component, Guid actorId, DateTimeOffset occurredAt, int sequence,
        ReserveTransactionType transactionType, decimal amount, decimal previousBalance, decimal newBalance, string reason,
        ReserveApprovalAuthority authority, bool needsOverride, Guid? replacesRejectedTransactionId)
        : base(component.OrganizationEntityId, actorId, occurredAt)
    {
        ClaimId = component.ClaimId;
        ReserveId = component.Id;
        ChangeSequence = sequence;
        TransactionType = transactionType;
        Amount = amount;
        PreviousBalance = previousBalance;
        NewBalance = newBalance;
        ChangeReason = reason;
        RequiredAuthority = authority;
        RequiresManagerOverride = needsOverride;
        ReplacesRejectedTransactionId = replacesRejectedTransactionId;
        var autoApproved = authority == ReserveApprovalAuthority.Automatic && !needsOverride &&
            !replacesRejectedTransactionId.HasValue;
        ApprovalStatus = autoApproved ? ReserveApprovalStatus.AutoApproved : ReserveApprovalStatus.PendingApproval;
        ApprovedAt = autoApproved ? occurredAt.ToUniversalTime() : null;
        PostingStatus = ReservePostingStatus.Pending;
        PostingKey = FormattableString.Invariant($"Reserve:{ReserveId:D}:Change:{sequence}");
    }

    public void Reject(ReserveApprovalAuthority authority, Guid actorId, DateTimeOffset occurredAt, string? reason)
    {
        EnsurePending();
        EnsureAuthority(authority);
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ClaimRuleViolation("RejectionReason", "A rejection reason is required.");
        }

        RejectionReason = reason.Trim();
        RejectedByUserId = actorId;
        RejectedAt = occurredAt.ToUniversalTime();
        ApprovalStatus = ReserveApprovalStatus.Rejected;
        PostingStatus = ReservePostingStatus.Cancelled;
        MarkUpdated(actorId, occurredAt);
    }

    public void Retract(Guid actorId, DateTimeOffset occurredAt)
    {
        EnsurePending();
        if (CreatedByUserId != actorId)
        {
            throw new ClaimRuleViolation("ApprovalStatus", "Only the submitter may retract this reserve transaction.");
        }

        ApprovalStatus = ReserveApprovalStatus.Cancelled;
        PostingStatus = ReservePostingStatus.Cancelled;
        MarkUpdated(actorId, occurredAt);
    }

    internal void EnsureCanApprove(ReserveApprovalAuthority authority, Guid actorId)
    {
        EnsurePending();
        if (CreatedByUserId == actorId)
        {
            throw new ClaimRuleViolation("ApprovalStatus", "Self-approval is not permitted.");
        }

        EnsureAuthority(authority);
    }

    internal void MarkApproved(Guid actorId, DateTimeOffset occurredAt)
    {
        ApprovalStatus = ReserveApprovalStatus.Approved;
        ApprovedByUserId = actorId;
        ApprovedAt = occurredAt.ToUniversalTime();
        MarkUpdated(actorId, occurredAt);
    }

    private void EnsureAuthority(ReserveApprovalAuthority authority)
    {
        if (authority != ReserveApprovalAuthority.Manager &&
            (authority != ReserveApprovalAuthority.Supervisor || RequiredAuthority == ReserveApprovalAuthority.Manager))
        {
            throw new ClaimRuleViolation("RequiredAuthority", "Your role does not have authority to review this reserve amount.");
        }
    }

    private void EnsurePending()
    {
        if (ApprovalStatus != ReserveApprovalStatus.PendingApproval)
        {
            throw new ClaimRuleViolation("ApprovalStatus", "Only pending reserve transactions can be reviewed or retracted.");
        }
    }
}
