using System.Text.Json.Serialization;
using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Reserves;

namespace ClaimsModule.Application.Reserves;

public sealed record InitialReserveRequest(ReserveComponentType? Component, decimal Amount);

public sealed record SubmitReserveRequest(ReserveComponentType? Component, decimal Amount, string? ChangeReason,
    ReserveTransactionType TransactionType = ReserveTransactionType.Add,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ReplacesRejectedTransactionId = null);

public sealed record AdjustReserveRequest(decimal Amount, string? ChangeReason,
    ReserveTransactionType TransactionType = ReserveTransactionType.Adjust, Guid? ReplacesRejectedTransactionId = null);

public sealed record RejectReserveRequest(string? RejectionReason);
public sealed record EnableReserveOverrideRequest(string? Reason);
public sealed record ReserveOverrideResult(Guid ClaimId, bool ReserveLimitOverrideEnabled);

public sealed record ReserveTransaction(Guid Id, Guid ReserveId, ReserveComponentType Component,
    ReserveTransactionType TransactionType, decimal Amount, decimal PreviousBalance, decimal NewBalance,
    string ChangeReason, int ChangeSequence, ReserveApprovalStatus ApprovalStatus, ReserveApprovalAuthority RequiredAuthority,
    bool RequiresManagerOverride, ReservePostingStatus PostingStatus, string PostingKey, string? PostingJobId,
    Guid? SubmittedByUserId, DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt)
{
    internal const string LimitWarningCode = "RESERVE_LIMIT_OVERRIDE_REQUIRED";
    internal const string LimitWarningMessage = "The projected approved total exceeds $10,000,000 and requires a manager override.";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ApprovedByUserId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? RejectedByUserId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? RejectedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RejectionReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? RetractedByUserId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? RetractedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ReplacesRejectedTransactionId { get; init; }

    public IReadOnlyList<ClaimWarningDto> Warnings => RequiresManagerOverride && ApprovalStatus == ReserveApprovalStatus.PendingApproval
        ? [new ClaimWarningDto(LimitWarningCode, "Amount", LimitWarningMessage)] : [];

    internal static ReserveTransaction From(ReserveHistory entry, ReserveComponentType component) =>
        new(entry.Id, entry.ReserveId, component, entry.TransactionType, entry.Amount, entry.PreviousBalance,
            entry.NewBalance, entry.ChangeReason, entry.ChangeSequence, entry.ApprovalStatus, entry.RequiredAuthority,
            entry.RequiresManagerOverride, entry.PostingStatus, entry.PostingKey, entry.PostingJobId,
            entry.CreatedByUserId, entry.CreatedAt, entry.ApprovedAt)
        {
            ApprovedByUserId = entry.ApprovedByUserId,
            RejectedByUserId = entry.RejectedByUserId,
            RejectedAt = entry.RejectedAt,
            RejectionReason = entry.RejectionReason,
            RetractedByUserId = entry.ApprovalStatus == ReserveApprovalStatus.Cancelled ? entry.UpdatedByUserId : null,
            RetractedAt = entry.ApprovalStatus == ReserveApprovalStatus.Cancelled ? entry.UpdatedAt : null,
            ReplacesRejectedTransactionId = entry.ReplacesRejectedTransactionId
        };
}

public sealed record ReserveBalance(Guid Id, ReserveComponentType Component, decimal CurrentAmount, decimal PendingAmount);

public sealed record ReserveComponentLedger(Guid Id, ReserveComponentType Component, decimal CurrentAmount,
    decimal PendingAmount, IReadOnlyList<ReserveTransaction> History);

public sealed record ClaimReserveLedger(Guid ClaimId, decimal TotalReserves, IReadOnlyList<ReserveComponentLedger> Components)
{
    public bool ReserveLimitOverrideEnabled { get; init; }
}
