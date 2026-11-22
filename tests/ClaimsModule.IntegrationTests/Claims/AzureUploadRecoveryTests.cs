using System.Net;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using ClaimsModule.Application.Claims.Documents;
using ClaimsModule.Application.Exceptions;
using ClaimsModule.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClaimsModule.IntegrationTests.Claims;

public sealed class AzureUploadRecoveryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeletesOnlyTheOwnedBlobAfterAnUncertainUpload(bool ownsBlob)
    {
        using var handler = new InterruptedBlobHandler(ownsBlob);
        using var client = new HttpClient(handler);
        var options = new BlobClientOptions { Transport = new HttpClientTransport(client) };
        options.Retry.MaxRetries = 0;
        var connection = $"DefaultEndpointsProtocol=https;AccountName=claimstest;AccountKey={Convert.ToBase64String(new byte[32])};EndpointSuffix=core.windows.net";
        var storage = new AzureBlobStorageService(new BlobContainerClient(connection, "claim-documents", options),
            NullLogger<AzureBlobStorageService>.Instance);
        using var content = new MemoryStream("text"u8.ToArray());
        var file = new ValidatedDocumentFile("report.txt", "text/plain", content.Length, "unused");
        await Assert.ThrowsAsync<DocumentStorageUnavailableException>(() =>
            storage.UploadAsync($"{Guid.NewGuid():D}/{Guid.NewGuid():D}/report.txt", file, content, default));
        Assert.NotNull(handler.UploadOwner);
        Assert.Equal(ownsBlob, handler.Deleted);
    }

    private sealed class InterruptedBlobHandler(bool ownsBlob) : HttpMessageHandler
    {
        public string? UploadOwner { get; private set; }
        public bool Deleted { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var container = request.RequestUri!.Query.Contains("restype=container", StringComparison.Ordinal);
            HttpResponseMessage response;
            if (container && request.Method == HttpMethod.Put)
            {
                response = new(HttpStatusCode.Created);
            }
            else if (container && request.Method == HttpMethod.Get)
            {
                response = new(HttpStatusCode.OK) { Content = new StringContent("<SignedIdentifiers/>") };
            }
            else if (request.Method == HttpMethod.Put)
            {
                Assert.Equal("*", request.Headers.IfNoneMatch.Single().Tag);
                UploadOwner = request.Headers.GetValues("x-ms-meta-claimupload").Single();
                response = new(HttpStatusCode.PreconditionFailed)
                {
                    Content = new StringContent("<Error><Code>ConditionNotMet</Code><Message>Conditional put failed</Message></Error>")
                };
                response.Headers.TryAddWithoutValidation("x-ms-error-code", "ConditionNotMet");
            }
            else if (request.Method == HttpMethod.Head)
            {
                response = new(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
                response.Headers.TryAddWithoutValidation("x-ms-meta-claimupload", ownsBlob ? UploadOwner : "other-owner");
            }
            else
            {
                Assert.Equal(HttpMethod.Delete, request.Method);
                Assert.Equal("\"observed\"", request.Headers.IfMatch.Single().Tag);
                Deleted = true;
                response = new(HttpStatusCode.Accepted);
            }

            response.Headers.TryAddWithoutValidation("ETag", "\"observed\"");
            response.Content.Headers.LastModified = DateTimeOffset.UtcNow;
            return Task.FromResult(response);
        }
    }
}
