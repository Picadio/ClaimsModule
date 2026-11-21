using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Exceptions;

namespace ClaimsModule.Domain.Reserves;

public sealed class ClaimReserveComponent : OrganizationRecord
{
    public const decimal MaximumAmount = 999_999_999_999_999.9999m;
    public const decimal ClaimApprovalLimit = 10_000_000m;
    public const int ChangeReasonMaximumLength = 500;

    public Guid ClaimId { get; private set; }
    public ReserveComponentType ComponentType { get; private set; }
    public decimal CurrentAmount { get; private set; }
    public int LastChangeSequence { get; private set; }

    private ClaimReserveComponent()
    {
    }

    public ClaimReserveComponent(Guid organizationId, Guid claimId, ReserveComponentType componentType, Guid actorId,
        DateTimeOffset occurredAt) : base(organizationId, actorId, occurredAt)
    {
        ClaimId = claimId;
        ComponentType = componentType;
    }

    public ReserveHistory Submit(ReserveChange change, decimal currentClaimTotal, bool hasPendingAdd, Guid actorId,
        DateTimeOffset occurredAt, bool hasLimitOverride = false, ReserveHistory? replaces = null)
    {
        var reason = change.ChangeReason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > ChangeReasonMaximumLength)
        {
            throw new ClaimRuleViolation("ChangeReason", $"Enter a reason of up to {ChangeReasonMaximumLength} characters.");
        }

        if (replaces is not null &&
            (!Owns(replaces) || replaces.ApprovalStatus != ReserveApprovalStatus.Rejected ||
             replaces.CreatedByUserId != actorId || replaces.TransactionType != change.TransactionType))
        {
            throw new ClaimRuleViolation("ReplacesRejectedTransactionId",
                "Select your rejected transaction of the same type and component.");
        }

        var projected = Project(change.TransactionType, change.Amount, hasPendingAdd);
        var needsOverride = !hasLimitOverride && currentClaimTotal + change.Amount > ClaimApprovalLimit;
        var magnitude = Math.Abs(change.Amount);
        var authority = magnitude > 100_000m ? ReserveApprovalAuthority.Manager
            : magnitude > 10_000m ? ReserveApprovalAuthority.Supervisor : ReserveApprovalAuthority.Automatic;
        var history = new ReserveHistory(this, actorId, occurredAt, ++LastChangeSequence, change.TransactionType,
            change.Amount, CurrentAmount, projected, reason, authority, needsOverride, replaces?.Id);

        if (history.ApprovalStatus == ReserveApprovalStatus.AutoApproved)
        {
            CurrentAmount = projected;
        }

        MarkUpdated(actorId, occurredAt);
        return history;
    }

    public void Approve(ReserveHistory transaction, ReserveApprovalAuthority authority, decimal currentClaimTotal,
        bool hasLimitOverride, Guid actorId, DateTimeOffset occurredAt)
    {
        if (!Owns(transaction))
        {
            throw new ClaimRuleViolation("ReserveId", "The transaction does not belong to this reserve.");
        }

        transaction.EnsureCanApprove(authority, actorId);
        var projected = Project(transaction.TransactionType, transaction.Amount, hasPendingAdd: false);
        if (!hasLimitOverride && currentClaimTotal + transaction.Amount > ClaimApprovalLimit)
        {
            throw new ClaimRuleViolation("Amount", "The projected approved total exceeds $10,000,000 and requires a manager override.");
        }

        transaction.MarkApproved(actorId, occurredAt);
        CurrentAmount = projected;
        MarkUpdated(actorId, occurredAt);
    }

    private bool Owns(ReserveHistory transaction) => transaction.ReserveId == Id &&
        transaction.ClaimId == ClaimId && transaction.OrganizationEntityId == OrganizationEntityId;

    private decimal Project(ReserveTransactionType transactionType, decimal amount, bool hasPendingAdd)
    {
        if (!Enum.IsDefined(transactionType))
        {
            throw new ClaimRuleViolation("TransactionType", "Select a valid reserve transaction type.");
        }

        if (amount == 0 || !IsRepresentable(amount))
        {
            throw new ClaimRuleViolation("Amount", "Enter a nonzero amount with up to four decimal places and 15 integer digits.");
        }

        if (transactionType == ReserveTransactionType.Add)
        {
            if (CurrentAmount != 0 || hasPendingAdd)
            {
                throw new ClaimRuleViolation("TransactionType", "An Add requires a zero balance and no pending Add.");
            }
        }
        else if (CurrentAmount == 0)
        {
            throw new ClaimRuleViolation("TransactionType", "Adjust and Reverse require a nonzero approved balance.");
        }

        if (transactionType == ReserveTransactionType.Reverse && amount != -CurrentAmount)
        {
            throw new ClaimRuleViolation("Amount", "A reversal must offset the entire approved component balance.");
        }

        var projected = CurrentAmount + amount;
        if (ComponentType == ReserveComponentType.SubrogationRecoverable ? projected > 0 : projected < 0)
        {
            throw new ClaimRuleViolation("Amount", "The change would give the component an invalid balance sign.");
        }

        if (!IsRepresentable(projected))
        {
            throw new ClaimRuleViolation("Amount", "The projected component balance exceeds the supported precision.");
        }

        return projected;
    }

    private static bool IsRepresentable(decimal amount) =>
        amount is >= -MaximumAmount and <= MaximumAmount && decimal.Round(amount, 4) == amount;
}
