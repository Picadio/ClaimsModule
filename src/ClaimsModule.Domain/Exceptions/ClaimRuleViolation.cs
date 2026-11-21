namespace ClaimsModule.Domain.Exceptions;

public sealed class ClaimRuleViolation : Exception
{
    public string Field => Errors[0].Field;
    public IReadOnlyList<ClaimRuleError> Errors { get; }

    public ClaimRuleViolation(string field, string message) : this([new ClaimRuleError(field, message)])
    {
    }

    internal ClaimRuleViolation(IReadOnlyList<ClaimRuleError> errors) : base(errors[0].Message)
    {
        Errors = errors;
    }
}

public sealed record ClaimRuleError(string Field, string Message);
