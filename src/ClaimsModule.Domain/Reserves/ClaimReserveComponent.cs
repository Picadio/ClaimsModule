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
        DateTimeOffset occurredAt)
    {
        var reason = change.ChangeReason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > ChangeReasonMaximumLength)
        {
            throw new ClaimRuleViolation("ChangeReason", $"Enter a reason of up to {ChangeReasonMaximumLength} characters.");
        }

        if (!Enum.IsDefined(change.TransactionType))
        {
            throw new ClaimRuleViolation("TransactionType", "Select a valid reserve transaction type.");
        }

        if (change.Amount == 0 || !IsRepresentable(change.Amount))
        {
            throw new ClaimRuleViolation("Amount", "Enter a nonzero amount with up to four decimal places and 15 integer digits.");
        }

        if (change.TransactionType == ReserveTransactionType.Add)
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

        if (change.TransactionType == ReserveTransactionType.Reverse && change.Amount != -CurrentAmount)
        {
            throw new ClaimRuleViolation("Amount", "A reversal must offset the entire approved component balance.");
        }

        var projected = CurrentAmount + change.Amount;
        if (ComponentType == ReserveComponentType.SubrogationRecoverable ? projected > 0 : projected < 0)
        {
            throw new ClaimRuleViolation("Amount", "The change would give the component an invalid balance sign.");
        }

        if (!IsRepresentable(projected))
        {
            throw new ClaimRuleViolation("Amount", "The projected component balance exceeds the supported precision.");
        }

        var needsOverride = currentClaimTotal + change.Amount > ClaimApprovalLimit;
        var magnitude = Math.Abs(change.Amount);
        var authority = magnitude > 100_000m ? ReserveApprovalAuthority.Manager
            : magnitude > 10_000m ? ReserveApprovalAuthority.Supervisor : ReserveApprovalAuthority.Automatic;
        var history = new ReserveHistory(this, actorId, occurredAt, ++LastChangeSequence, change.TransactionType,
            change.Amount, CurrentAmount, projected, reason, authority, needsOverride);

        if (history.ApprovalStatus == ReserveApprovalStatus.AutoApproved)
        {
            CurrentAmount = projected;
        }

        MarkUpdated(actorId, occurredAt);
        return history;
    }

    private static bool IsRepresentable(decimal amount) =>
        amount is >= -MaximumAmount and <= MaximumAmount && decimal.Round(amount, 4) == amount;
}
