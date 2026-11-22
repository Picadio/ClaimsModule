namespace ClaimsModule.Application.Exceptions;

public sealed class DocumentStorageUnavailableException(string message) : Exception(message);
