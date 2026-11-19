namespace ClaimsModule.Domain.Exceptions;

public sealed class ClaimRuleViolation(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}
