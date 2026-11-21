namespace ClaimsModule.Domain.Claims;

public sealed record ClaimTransitionDetails(string? Reason, Guid? AssignedHandlerId,
    bool AcknowledgePolicyPeriodWarning, bool ConfirmPositiveReserveBalance, bool LiabilityDetermined);

public sealed record ClaimLifecycleFacts(bool CauseOfLossIsActive, ClaimReserveFacts Reserves);

public readonly record struct ClaimReserveFacts(bool HasApprovedReserve, bool HasPendingApproval,
    bool HasPositiveComponentBalance);

public sealed record ClaimStatusChange(ClaimStatus PreviousStatus, ClaimStatus TargetStatus, ClaimStatus FinalStatus,
    Guid? PreviousHandlerId, string? Reason, ClaimWarning? AcknowledgedWarning, bool PositiveBalanceConfirmed,
    bool LiabilityDetermined);
