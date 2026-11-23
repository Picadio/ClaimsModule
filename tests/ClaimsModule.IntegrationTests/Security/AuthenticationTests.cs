using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClaimsModule.API.Security;
using ClaimsModule.Application.Security;
using ClaimsModule.IntegrationTests.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ClaimsModule.IntegrationTests.Security;

public sealed class AuthenticationTests(ClaimsApiFactory factory) : IClassFixture<ClaimsApiFactory>
{
    private const string DemoTokenPath = "/api/auth/demo-token";
    private const string MePath = "/api/auth/me";
    private const string SupervisorPath = "/test/authorization/supervisor";
    private const string ManagerPath = "/test/authorization/manager";

    [Theory]
    [InlineData(UserRoles.Handler)]
    [InlineData(UserRoles.Supervisor)]
    [InlineData(UserRoles.Manager)]
    public async Task DemoTokenAuthenticatesTheConfiguredAccount(string role)
    {
        using var client = factory.CreateApiClient();
        var account = factory.Account(role);

        using var login = await Login(client, role, account.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(login.Headers.CacheControl?.NoStore);

        var token = await login.Content.ReadFromJsonAsync<DemoTokenResponse>();
        Assert.NotNull(token);
        Assert.Equal("Bearer", token.TokenType);
        Assert.Equal(1800, token.ExpiresIn);

        using var response = await Get(client, MePath, token.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var identity = await response.Content.ReadFromJsonAsync<UserIdentity>();
        Assert.Equal(new UserIdentity(account.UserId, factory.OrganizationId, role), identity);
    }

    [Fact]
    public async Task UserDirectoryListsOnlyPublicFieldsForTheCurrentTenant()
    {
        using var client = factory.CreateApiClient();
        await FnolApiTests.Authenticate(client, factory, UserRoles.Manager);
        using var response = await client.GetAsync("/api/reference/users");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var users = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(3, users.Length);
        foreach (var user in users)
        {
            Assert.Equal(3, user.EnumerateObject().Count());
            var role = user.GetProperty("role").GetString()!;
            Assert.Equal(factory.Account(role).UserId, user.GetProperty("userId").GetGuid());
            Assert.False(string.IsNullOrWhiteSpace(user.GetProperty("displayName").GetString()));
        }
    }

    [Theory]
    [InlineData(UserRoles.Handler)]
    [InlineData("unknown")]
    public async Task DemoTokenRejectsInvalidCredentials(string username)
    {
        using var client = factory.CreateApiClient();
        using var response = await Login(client, username, "incorrect-password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DemoTokenIsUnavailableWhenDisabled()
    {
        await using var disabledFactory = new ClaimsApiFactory(demoEnabled: false);
        using var client = disabledFactory.CreateApiClient();
        var account = disabledFactory.Account(UserRoles.Handler);

        using var response = await Login(client, UserRoles.Handler, account.Password);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task DemoRegistrationRequiresBothFlags(bool demoEnabled, bool allowRegistration)
    {
        await using var api = new ClaimsApiFactory(demoEnabled, allowRegistration: allowRegistration);
        using var client = api.CreateApiClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterDemoUserRequest("Maya", "Example-password", UserRoles.Manager));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("ValidName", "Example-password", "admin")]
    [InlineData("ValidName", "short", UserRoles.Handler)]
    [InlineData("Invalid:Name", "Example-password", UserRoles.Supervisor)]
    public async Task DemoRegistrationValidatesRolesAndCredentials(string username, string password, string role)
    {
        await using var api = new ClaimsApiFactory(demoEnabled: true, allowRegistration: true);
        using var client = api.CreateApiClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterDemoUserRequest(username, password, role));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task DemoRegistrationCanReplayAndSignInWithSelectedRole()
    {
        await using var api = new ClaimsApiFactory(demoEnabled: true, allowRegistration: true);
        using var client = api.CreateApiClient();
        var account = new RegisterDemoUserRequest("Maya", "Example-password", UserRoles.Manager);
        var registrationKey = Guid.NewGuid().ToString();
        using var first = await Register(client, account, registrationKey);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var user = await first.Content.ReadFromJsonAsync<UserReference>();
        Assert.Equal("Maya", user!.DisplayName);
        Assert.Equal(UserRoles.Manager, user.Role);
        using var replay = await Register(client, account, registrationKey);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(user, await replay.Content.ReadFromJsonAsync<UserReference>());
        using var changed = await Register(client, account with { Role = UserRoles.Handler }, registrationKey);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var duplicate = await Register(client, account with { Username = "MAYA" }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var login = await Login(client, "maya", account.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = await login.Content.ReadFromJsonAsync<DemoTokenResponse>();
        using var managerOnly = await Get(client, ManagerPath, token!.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, managerOnly.StatusCode);
    }

    internal static async Task<HttpResponseMessage> Register(HttpClient client, RegisterDemoUserRequest account, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register") { Content = JsonContent.Create(account) };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task DemoTokenLimitsRepeatedLoginAttempts()
    {
        await using var rateLimitFactory = new ClaimsApiFactory();
        using var client = rateLimitFactory.CreateApiClient();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var failure = await Login(client, "unknown", "incorrect-password");
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        using var response = await Login(client, "unknown", "incorrect-password");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("malformed-token")]
    public async Task CurrentUserChallengesRequestsWithoutAValidBearerToken(string? token)
    {
        using var client = factory.CreateApiClient();
        using var response = await Get(client, MePath, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, challenge => challenge.Scheme == "Bearer");
    }

    [Theory]
    [InlineData(TokenProblem.Expired)]
    [InlineData(TokenProblem.WrongIssuer)]
    [InlineData(TokenProblem.WrongAudience)]
    [InlineData(TokenProblem.WrongSignature)]
    [InlineData(TokenProblem.MissingOrganization)]
    [InlineData(TokenProblem.UnsupportedRole)]
    [InlineData(TokenProblem.MultipleRoles)]
    public async Task CurrentUserRejectsInvalidJwt(TokenProblem problem)
    {
        using var client = factory.CreateApiClient();
        var token = CreateToken(UserRoles.Handler, problem);

        using var response = await Get(client, MePath, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRoles.Handler, SupervisorPath, HttpStatusCode.Forbidden)]
    [InlineData(UserRoles.Supervisor, SupervisorPath, HttpStatusCode.NoContent)]
    [InlineData(UserRoles.Manager, SupervisorPath, HttpStatusCode.NoContent)]
    [InlineData(UserRoles.Handler, ManagerPath, HttpStatusCode.Forbidden)]
    [InlineData(UserRoles.Supervisor, ManagerPath, HttpStatusCode.Forbidden)]
    [InlineData(UserRoles.Manager, ManagerPath, HttpStatusCode.NoContent)]
    public async Task AuthorizationPolicyEnforcesTheRole(string role, string path, HttpStatusCode expected)
    {
        using var client = factory.CreateApiClient();
        var token = CreateToken(role);

        using var response = await Get(client, path, token);

        Assert.Equal(expected, response.StatusCode);
    }

    private string CreateToken(string role, TokenProblem? problem = null)
    {
        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = factory.Account(role).UserId.ToString("D"),
            ["org_id"] = factory.OrganizationId.ToString("D"),
            ["role"] = role
        };
        var token = new SecurityTokenDescriptor
        {
            Issuer = ClaimsApiFactory.Issuer,
            Audience = ClaimsApiFactory.Audience,
            IssuedAt = now.AddMinutes(-2),
            NotBefore = now.AddMinutes(-2),
            Expires = now.AddMinutes(5),
            SigningCredentials = SignWith(factory.SigningKey),
            Claims = claims
        };

        switch (problem)
        {
            case TokenProblem.Expired:
                token.Expires = now.AddMinutes(-1);
                break;
            case TokenProblem.WrongIssuer:
                token.Issuer = "other-issuer";
                break;
            case TokenProblem.WrongAudience:
                token.Audience = "other-audience";
                break;
            case TokenProblem.WrongSignature:
                token.SigningCredentials = SignWith(RandomNumberGenerator.GetBytes(32));
                break;
            case TokenProblem.MissingOrganization:
                claims.Remove("org_id");
                break;
            case TokenProblem.UnsupportedRole:
                claims["role"] = "unknown";
                break;
            case TokenProblem.MultipleRoles:
                claims["role"] = new[] { UserRoles.Handler, UserRoles.Manager };
                break;
        }

        return new JsonWebTokenHandler().CreateToken(token);
    }

    private static SigningCredentials SignWith(byte[] key) =>
        new(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);

    private static Task<HttpResponseMessage> Login(HttpClient client, string username, string password) =>
        client.PostAsJsonAsync(DemoTokenPath, new { username, password });

    private static async Task<HttpResponseMessage> Get(HttpClient client, string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }

    public enum TokenProblem
    {
        Expired,
        WrongIssuer,
        WrongAudience,
        WrongSignature,
        MissingOrganization,
        UnsupportedRole,
        MultipleRoles
    }
}
