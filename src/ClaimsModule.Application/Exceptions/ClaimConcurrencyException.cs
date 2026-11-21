namespace ClaimsModule.Application.Exceptions;

public sealed class ClaimConcurrencyException(Exception innerException)
    : Exception("The claim was updated by another request. Reload its current status and retry.", innerException);
