using System.Net;
using System.Text.Json;
using ClaimsModule.IntegrationTests.Security;
using Xunit;

namespace ClaimsModule.IntegrationTests.OpenApi;

public sealed class SwaggerTests(ClaimsApiFactory factory) : IClassFixture<ClaimsApiFactory>
{
    private const string SwaggerIndex = "/swagger/index.html";
    private const string DocumentPath = "/openapi/v1.json";

    [Fact]
    public async Task SwaggerLoadsThePageAndConfiguresTheExistingDocument()
    {
        using var client = factory.CreateApiClient();

        using var redirect = await client.GetAsync("/swagger");
        Assert.Equal(HttpStatusCode.MovedPermanently, redirect.StatusCode);
        Assert.NotNull(redirect.Headers.Location);
        var page = new Uri(new Uri(client.BaseAddress!, "/swagger"), redirect.Headers.Location);
        Assert.Equal(SwaggerIndex, page.AbsolutePath);

        using var html = await client.GetAsync(page);
        Assert.Equal(HttpStatusCode.OK, html.StatusCode);
        Assert.Equal("text/html", html.Content.Headers.ContentType?.MediaType);

        var initializer = await client.GetStringAsync("/swagger/index.js");
        Assert.Contains(".." + DocumentPath, initializer);
    }

    [Fact]
    public async Task OpenApiDocumentsBearerAuthForProtectedOperations()
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync(DocumentPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        var bearer = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());

        var paths = root.GetProperty("paths");
        var me = paths.GetProperty("/api/auth/me").GetProperty("get");
        Assert.Contains(me.GetProperty("security").EnumerateArray(),
            requirement => requirement.TryGetProperty("Bearer", out _));

        var login = paths.GetProperty("/api/auth/demo-token").GetProperty("post");
        Assert.False(login.TryGetProperty("security", out var security) && security.GetArrayLength() > 0);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(true, HttpStatusCode.OK)]
    public async Task ProductionRequiresOptInToExposeTheUiAndDocument(bool? enabled, HttpStatusCode expected)
    {
        await using var production = new ClaimsApiFactory(demoEnabled: false,
            environment: Environments.Production, openApiEnabled: enabled);
        using var client = production.CreateApiClient();

        using var page = await client.GetAsync(SwaggerIndex);
        using var document = await client.GetAsync(DocumentPath);

        Assert.Equal(expected, page.StatusCode);
        Assert.Equal(expected, document.StatusCode);
    }
}
