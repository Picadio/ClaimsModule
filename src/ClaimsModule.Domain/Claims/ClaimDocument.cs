using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Claims;

public enum DocumentStorageProvider { Unavailable, AzureBlob, LocalFileSystem }

public sealed class ClaimDocument : OrganizationRecord
{
    public Guid ClaimId { get; private set; }
    public string FileName { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public DocumentStorageProvider StorageProvider { get; private set; }
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public string DocumentType { get; private set; } = null!;
    public string? Notes { get; private set; }

    private ClaimDocument()
    {
    }

    public ClaimDocument(Guid organizationId, Guid claimId, Guid actorId, DateTimeOffset now, string fileName,
        string storageKey, DocumentStorageProvider provider, string contentType, long size, string documentType, string? notes)
        : base(organizationId, actorId, now)
    {
        ClaimId = claimId;
        FileName = fileName;
        StorageKey = storageKey;
        StorageProvider = provider;
        ContentType = contentType;
        SizeBytes = size;
        DocumentType = documentType;
        Notes = notes;
    }
}
