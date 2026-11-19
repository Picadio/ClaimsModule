namespace ClaimsModule.Application.Exceptions;

public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException()
        : base("This Idempotency-Key was already used with a different claim request.")
    {
    }
}
