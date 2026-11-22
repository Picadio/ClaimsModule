using System.Net;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using ClaimsModule.Application.Claims.Documents;
using ClaimsModule.Domain.Exceptions;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Infrastructure.Storage;
using ClaimsModule.IntegrationTests.Security;
using Xunit;

namespace ClaimsModule.IntegrationTests.Claims;

public sealed class ClaimDocumentTests(ClaimsApiFactory factory) : IClassFixture<ClaimsApiFactory>
{
    [Theory]
    [InlineData("invoice.png", "image/png", "%PDF-1.7")]
    [InlineData("invoice.exe", "application/octet-stream", "%PDF-1.7")]
    [InlineData("invoice.pdf", "image/png", "%PDF-1.7")]
    [InlineData("workbook.xlsx", "application/octet-stream", "PK")]
    public async Task RejectsUnsupportedFormatsAndMismatchedContent(string name, string mime, string body)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        await Assert.ThrowsAsync<ClaimRuleViolation>(() => DocumentFileRules.ValidateAsync(name, mime, stream, default));
    }

    [Fact]
    public async Task AcceptsAnOfficePackageWithExpectedParts()
    {
        using var stream = new MemoryStream(OfficeBytes());
        var file = await DocumentFileRules.ValidateAsync("report.docx", "application/octet-stream", stream, default);
        Assert.Equal("report.docx", file.FileName);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task RejectsAnOfficeDirectoryPointingOutsideTheLocalParts()
    {
        var bytes = OfficeBytes();
        var directory = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(bytes.Length - 6));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(directory + 42), (uint)directory);
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<ClaimRuleViolation>(() =>
            DocumentFileRules.ValidateAsync("report.docx", "application/octet-stream", stream, default));
    }

    [Fact]
    public async Task FileJustAboveTheBusinessLimitGetsAValidationResponse()
    {
        using var client = factory.CreateApiClient();
        await FnolApiTests.Authenticate(client, factory);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(new byte[(int)DocumentFileRules.MaxBytes + 1]);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "over-limit.txt");
        using var response = await client.PostAsync($"/api/claims/{Guid.NewGuid():D}/documents", form);
        await ReserveApiTests.AssertError(response, "File");
    }

    [Fact]
    public void AzureReadLinkIsScopedToOneBlobWithReadOnlyHttpsPermissions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var connection = $"DefaultEndpointsProtocol=https;AccountName=claimstest;AccountKey={Convert.ToBase64String(new byte[32])};EndpointSuffix=core.windows.net";
        services.AddClaimDocumentStorage(DocumentStorageProvider.AzureBlob, connection, Path.GetTempPath());
        using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IStorageService>();
        var key = $"{Guid.NewGuid():D}/{Guid.NewGuid():D}/report.pdf";
        var expires = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var link = storage.CreateReadLink(key, expires);
        Assert.NotNull(link);
        Assert.Equal($"/claim-documents/{key}", link.AbsolutePath);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(link.Query);
        Assert.Equal("r", query["sp"].ToString());
        Assert.Equal("b", query["sr"].ToString());
        Assert.Equal("https", query["spr"].ToString());
        Assert.Equal(expires, DateTimeOffset.Parse(query["se"].ToString()));
    }

    [Fact]
    public async Task LocalFilesUseSafeNamesAndCannotOverwriteAnExistingFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "claims-doc-" + Guid.NewGuid().ToString("N"));
        var organization = Guid.NewGuid().ToString("D");
        var claim = Guid.NewGuid().ToString("D");
        var bytes = "%PDF-1.7 test"u8.ToArray();
        using var stream = new MemoryStream(bytes);
        var file = await DocumentFileRules.ValidateAsync(@"C:\fakepath\Loss report.PDF", "application/pdf", stream, default);
        var storage = new LocalFileSystemStorageService(root);
        var key = $"{organization}/{claim}/{file.FileName}";
        try
        {
            Assert.Equal("Loss_report.pdf", file.FileName);
            Assert.Equal(0, stream.Position);
            await storage.UploadAsync(key, file, stream, default);
            stream.Position = 0;
            await Assert.ThrowsAsync<IOException>(() => storage.UploadAsync(key, file, stream, default));
            await using var input = storage.OpenLocalRead(key);
            Assert.NotNull(input);
            using var output = new MemoryStream();
            await input.CopyToAsync(output);
            Assert.Equal(bytes, output.ToArray());
            Assert.Throws<ArgumentException>(() => storage.OpenLocalRead($"{organization}/{claim}/../alternate.pdf"));
        }
        finally
        {
            await storage.DeleteAsync(key, default);
            foreach (var directory in new[] { Path.Combine(root, organization, claim), Path.Combine(root, organization), root })
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory);
                }
            }
        }
    }

    [Fact]
    public async Task DocumentUploadRequiresJwtAndRejectsInvalidFormatsBeforeDatabaseAccess()
    {
        var path = $"/api/claims/{Guid.NewGuid():D}/documents";
        using var client = factory.CreateApiClient();
        using var unauthorized = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        await FnolApiTests.Authenticate(client, factory);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("fake"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "invalid.pdf");
        using var invalid = await client.PostAsync(path, form);
        await ReserveApiTests.AssertError(invalid, "File");
    }

    private static byte[] OfficeBytes()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var part in new[] { "[Content_Types].xml", "word/document.xml" })
            {
                using var content = zip.CreateEntry(part).Open();
                content.Write("<root/>"u8);
            }
        }

        return stream.ToArray();
    }
}
