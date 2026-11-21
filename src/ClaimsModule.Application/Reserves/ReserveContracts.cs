using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Reserves;

namespace ClaimsModule.Application.Reserves;

public sealed record InitialReserveRequest(ReserveComponentType? Component, decimal Amount);

public sealed record SubmitReserveRequest(ReserveComponentType? Component, decimal Amount, string? ChangeReason,
    ReserveTransactionType TransactionType = ReserveTransactionType.Add);

public sealed record AdjustReserveRequest(decimal Amount, string? ChangeReason,
    ReserveTransactionType TransactionType = ReserveTransactionType.Adjust);

public sealed record ReserveTransaction(Guid Id, Guid ReserveId, ReserveComponentType Component,
    ReserveTransactionType TransactionType, decimal Amount, decimal PreviousBalance, decimal NewBalance,
    string ChangeReason, int ChangeSequence, ReserveApprovalStatus ApprovalStatus, ReserveApprovalAuthority RequiredAuthority,
    bool RequiresManagerOverride, ReservePostingStatus PostingStatus, string PostingKey, string? PostingJobId,
    Guid? SubmittedByUserId, DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt)
{
    internal const string LimitWarningCode = "RESERVE_LIMIT_OVERRIDE_REQUIRED";
    internal const string LimitWarningMessage = "The projected approved total exceeds $10,000,000 and requires a manager override.";

    public IReadOnlyList<ClaimWarningDto> Warnings => RequiresManagerOverride
        ? [new ClaimWarningDto(LimitWarningCode, "Amount", LimitWarningMessage)] : [];

    internal static ReserveTransaction From(ReserveHistory entry, ReserveComponentType component) =>
        new(entry.Id, entry.ReserveId, component, entry.TransactionType, entry.Amount, entry.PreviousBalance,
            entry.NewBalance, entry.ChangeReason, entry.ChangeSequence, entry.ApprovalStatus, entry.RequiredAuthority,
            entry.RequiresManagerOverride, entry.PostingStatus, entry.PostingKey, entry.PostingJobId,
            entry.CreatedByUserId, entry.CreatedAt, entry.ApprovedAt);
}

public sealed record ReserveBalance(Guid Id, ReserveComponentType Component, decimal CurrentAmount, decimal PendingAmount);

public sealed record ReserveComponentLedger(Guid Id, ReserveComponentType Component, decimal CurrentAmount,
    decimal PendingAmount, IReadOnlyList<ReserveTransaction> History);

public sealed record ClaimReserveLedger(Guid ClaimId, decimal TotalReserves, IReadOnlyList<ReserveComponentLedger> Components);
