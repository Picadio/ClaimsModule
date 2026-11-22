using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using ClaimsModule.Application.Claims.Documents;
using ClaimsModule.Application.Exceptions;
using ClaimsModule.Domain.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClaimsModule.Infrastructure.Storage;

public static class ClaimDocumentStorage
{
    public static IServiceCollection AddClaimDocumentStorage(this IServiceCollection services,
        DocumentStorageProvider provider, string? blobConnection, string localRoot)
    {
        return services.AddSingleton<IStorageService>(serviceProvider => provider switch
        {
            DocumentStorageProvider.AzureBlob when !string.IsNullOrWhiteSpace(blobConnection) =>
                new AzureBlobStorageService(new BlobContainerClient(blobConnection, "claim-documents"),
                    serviceProvider.GetRequiredService<ILogger<AzureBlobStorageService>>()),
            DocumentStorageProvider.LocalFileSystem => new LocalFileSystemStorageService(localRoot),
            _ => new UnavailableStorageService()
        });
    }
}

internal sealed class AzureBlobStorageService(BlobContainerClient container, ILogger<AzureBlobStorageService> logger)
    : IStorageService
{
    private const string UploadOwner = "claimupload";

    public DocumentStorageProvider Provider => DocumentStorageProvider.AzureBlob;

    public async Task UploadAsync(string key, ValidatedDocumentFile file, Stream content, CancellationToken cancellationToken)
    {
        try
        {
            var blob = container.GetBlobClient(key);
            if (!blob.CanGenerateSasUri)
            {
                throw new DocumentStorageUnavailableException("Azure document storage cannot sign download links.");
            }

            await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
            var access = await container.GetAccessPolicyAsync(cancellationToken: cancellationToken);
            if (access.Value.BlobPublicAccess != PublicAccessType.None)
            {
                throw new DocumentStorageUnavailableException("The claim documents container must be private.");
            }

            var owner = Guid.NewGuid().ToString("N");
            try
            {
                await blob.UploadAsync(content, new BlobUploadOptions
                {
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                    Metadata = new Dictionary<string, string> { [UploadOwner] = owner },
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = file.ContentType,
                        ContentDisposition = $"attachment; filename=\"{file.FileName}\""
                    }
                }, cancellationToken);
            }
            catch
            {
                await DeleteOwnedBlobAsync(blob, owner);
                throw;
            }
        }
        catch (RequestFailedException)
        {
            throw new DocumentStorageUnavailableException("Azure document storage is unavailable.");
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    public Uri CreateReadLink(string key, DateTimeOffset expiresAt)
    {
        var blob = container.GetBlobClient(key);
        if (!blob.CanGenerateSasUri)
        {
            throw new DocumentStorageUnavailableException("Azure document storage cannot sign download links.");
        }

        var sas = new BlobSasBuilder(BlobSasPermissions.Read, expiresAt)
        {
            BlobContainerName = container.Name,
            BlobName = key,
            Resource = "b",
            Protocol = SasProtocol.Https
        };
        return blob.GenerateSasUri(sas);
    }

    public Stream? OpenLocalRead(string key) => null;

    private async Task DeleteOwnedBlobAsync(BlobClient blob, string owner)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            BlobProperties properties;
            try
            {
                properties = (await blob.GetPropertiesAsync(cancellationToken: timeout.Token)).Value;
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token);
                properties = (await blob.GetPropertiesAsync(cancellationToken: timeout.Token)).Value;
            }

            if (properties.Metadata.TryGetValue(UploadOwner, out var storedOwner) && storedOwner == owner)
            {
                await blob.DeleteIfExistsAsync(conditions: new BlobRequestConditions { IfMatch = properties.ETag },
                    cancellationToken: timeout.Token);
            }
        }
        catch (RequestFailedException exception) when (exception.Status is 404 or 412)
        {
            // There is no owned object at the observed version to delete.
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Azure could not confirm cleanup for an interrupted claim document upload.");
        }
    }
}

public sealed class LocalFileSystemStorageService(string root) : IStorageService
{
    private readonly string root = Path.GetFullPath(root);

    public DocumentStorageProvider Provider => DocumentStorageProvider.LocalFileSystem;

    public async Task UploadAsync(string key, ValidatedDocumentFile file, Stream content, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        var created = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                FileOptions.Asynchronous);
            created = true;
            await content.CopyToAsync(destination, cancellationToken);
        }
        catch
        {
            if (created)
            {
                File.Delete(path);
            }

            throw;
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(Resolve(key));
        return Task.CompletedTask;
    }

    public Uri? CreateReadLink(string key, DateTimeOffset expiresAt) => null;

    public Stream? OpenLocalRead(string key)
    {
        try
        {
            return new FileStream(Resolve(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private string Resolve(string key)
    {
        var parts = key.Split('/');
        if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "D", out _) || !Guid.TryParseExact(parts[1], "D", out _) ||
            parts[2] is "" or "." or ".." || !parts[2].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            throw new ArgumentException("The claim document storage key is invalid.", nameof(key));
        }

        return Path.Combine(root, parts[0], parts[1], parts[2]);
    }
}

internal sealed class UnavailableStorageService : IStorageService
{
    public DocumentStorageProvider Provider => DocumentStorageProvider.Unavailable;

    public Task UploadAsync(string key, ValidatedDocumentFile file, Stream content, CancellationToken cancellationToken) =>
        throw new DocumentStorageUnavailableException("Configure a claim document storage provider.");

    public Task DeleteAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;

    public Uri? CreateReadLink(string key, DateTimeOffset expiresAt) => null;

    public Stream? OpenLocalRead(string key) => null;
}
