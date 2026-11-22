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
    public Guid? PostingCorrelationId { get; private set; }
    public bool IsPendingPosting => PostingStatus == ReservePostingStatus.Pending && IsApprovedForPosting;

    private bool IsApprovedForPosting => ApprovalStatus is ReserveApprovalStatus.AutoApproved or ReserveApprovalStatus.Approved;

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

    public void CapturePostingCorrelation(Guid correlationId)
    {
        if (IsPendingPosting && PostingJobId is null)
        {
            PostingCorrelationId = correlationId;
        }
    }

    public void AssignPostingJob(string jobId, Guid fallbackCorrelationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        if (!IsPendingPosting || PostingJobId is not null)
        {
            throw new InvalidOperationException("The reserve does not have an unassigned pending posting.");
        }

        PostingJobId = jobId;
        PostingCorrelationId ??= fallbackCorrelationId;
    }

    public bool TryFinishPosting(string jobId, string postingKey, bool succeeded)
    {
        if (!IsPendingPosting || PostingJobId is null || PostingJobId != jobId || PostingKey != postingKey)
        {
            return false;
        }

        PostingStatus = succeeded ? ReservePostingStatus.Posted : ReservePostingStatus.Failed;
        return true;
    }

    public void RetryPosting(Guid correlationId)
    {
        if (!IsApprovedForPosting || PostingStatus != ReservePostingStatus.Failed)
        {
            throw new ClaimRuleViolation("PostingStatus", "Only failed postings on approved reserve transactions can be retried.");
        }

        PostingStatus = ReservePostingStatus.Pending;
        PostingJobId = null;
        PostingCorrelationId = correlationId;
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
