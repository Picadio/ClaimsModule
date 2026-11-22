using ClaimsModule.Domain.Claims;

namespace ClaimsModule.Application.Claims.Documents;

public interface IStorageService
{
    DocumentStorageProvider Provider { get; }
    Task UploadAsync(string key, ValidatedDocumentFile file, Stream content, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
    Uri? CreateReadLink(string key, DateTimeOffset expiresAt);
    Stream? OpenLocalRead(string key);
}

public interface IClaimDocumentStore
{
    Task<IReadOnlyList<ClaimDocument>?> ListAsync(Guid claimId, CancellationToken cancellationToken);
    Task<ClaimDocument?> FindAsync(Guid claimId, Guid documentId, CancellationToken cancellationToken);
    void Add(ClaimDocument document);
}

public sealed record ClaimDocumentDto(Guid Id, string FileName, string DocumentType, string ContentType, long SizeBytes,
    string? Notes, DateTimeOffset CreatedAt, Guid? UploadedByUserId, string? DownloadUrl);

public sealed record LocalClaimDocument(string FileName, string ContentType, Stream Content);

internal sealed class DocumentLinks(IStorageService storage, TimeProvider clock)
{
    public ClaimDocumentDto Create(ClaimDocument document) =>
        new(document.Id, document.FileName, document.DocumentType, document.ContentType, document.SizeBytes,
            document.Notes, document.CreatedAt, document.CreatedByUserId, ReadLink(document));

    private string? ReadLink(ClaimDocument document)
    {
        if (document.StorageProvider != storage.Provider)
        {
            return null;
        }

        return storage.Provider == DocumentStorageProvider.LocalFileSystem
            ? $"/api/claims/{document.ClaimId:D}/documents/{document.Id:D}/download"
            : storage.CreateReadLink(document.StorageKey, clock.GetUtcNow().AddHours(1))?.AbsoluteUri;
    }
}
