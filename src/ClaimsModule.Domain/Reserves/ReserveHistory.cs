using ClaimsModule.Domain.Common;

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
    public ReservePostingStatus PostingStatus { get; private set; }
    public string PostingKey { get; private set; } = null!;
    public string? PostingJobId { get; private set; }

    private ReserveHistory()
    {
    }

    internal ReserveHistory(ClaimReserveComponent component, Guid actorId, DateTimeOffset occurredAt, int sequence,
        ReserveTransactionType transactionType, decimal amount, decimal previousBalance, decimal newBalance, string reason,
        ReserveApprovalAuthority authority, bool needsOverride)
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
        var autoApproved = authority == ReserveApprovalAuthority.Automatic && !needsOverride;
        ApprovalStatus = autoApproved ? ReserveApprovalStatus.AutoApproved : ReserveApprovalStatus.PendingApproval;
        ApprovedAt = autoApproved ? occurredAt.ToUniversalTime() : null;
        PostingStatus = ReservePostingStatus.Pending;
        PostingKey = FormattableString.Invariant($"Reserve:{ReserveId:D}:Change:{sequence}");
    }
}
