namespace ClaimsModule.Domain.Reserves;

public enum ReserveComponentType { Indemnity = 1, Expense, ALAE, SubrogationRecoverable }
public enum ReserveTransactionType { Add = 1, Adjust, Reverse }
public enum ReserveApprovalStatus { AutoApproved = 1, PendingApproval, Approved, Rejected, Cancelled }
public enum ReserveApprovalAuthority { Automatic = 1, Supervisor, Manager }
public enum ReservePostingStatus { Pending = 1, Posted, Failed, Cancelled }

public sealed record ReserveChange(ReserveTransactionType TransactionType, decimal Amount, string? ChangeReason);
