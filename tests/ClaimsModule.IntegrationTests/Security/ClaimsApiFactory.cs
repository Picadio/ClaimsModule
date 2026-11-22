using System.Security.Cryptography;
using ClaimsModule.API.Controllers;
using ClaimsModule.Application.Security;
using ClaimsModule.Application.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace ClaimsModule.IntegrationTests.Security;

// AuthController anchors WebApplicationFactory to the API's entry-point assembly.
public sealed class ClaimsApiFactory : WebApplicationFactory<AuthController>
{
    internal const string Issuer = "ClaimsModule.IntegrationTests";
    internal const string Audience = "ClaimsModule.IntegrationTests.API";

    private readonly bool demoEnabled;
    private readonly string environment;
    private readonly string? claimsConnection;
    private readonly bool openApiEnabled;
    private readonly bool allowDirectClosure;
    private readonly bool glJobsEnabled;
    private readonly string? handlerDisplayName;
    private readonly bool useDatabaseUsers;
    private readonly string? legacyHandlerId;
    private readonly Dictionary<string, DemoAccount> accounts =
        new[] { UserRoles.Handler, UserRoles.Supervisor, UserRoles.Manager }
            .ToDictionary(role => role, _ => new DemoAccount(Guid.NewGuid(), RandomSecret()));

    internal byte[] SigningKey { get; } = RandomNumberGenerator.GetBytes(32);
    internal Guid OrganizationId { get; }

    public ClaimsApiFactory() : this(demoEnabled: true)
    {
    }

    internal ClaimsApiFactory(bool demoEnabled, string? environment = null,
        string? claimsConnection = null, Guid? organizationId = null, bool openApiEnabled = false,
        bool allowDirectClosure = false, bool glJobsEnabled = false, string? handlerDisplayName = null,
        bool useDatabaseUsers = false, string? legacyHandlerId = null)
    {
        this.demoEnabled = demoEnabled;
        this.environment = environment ?? Environments.Development;
        this.claimsConnection = claimsConnection;
        this.openApiEnabled = openApiEnabled;
        this.allowDirectClosure = allowDirectClosure;
        this.glJobsEnabled = glJobsEnabled;
        this.handlerDisplayName = handlerDisplayName;
        this.useDatabaseUsers = useDatabaseUsers;
        this.legacyHandlerId = legacyHandlerId;
        OrganizationId = organizationId ?? Guid.NewGuid();
    }

    internal DemoAccount Account(string role) => accounts[role];

    internal HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Authentication:Jwt:Issuer"] = Issuer,
            ["Authentication:Jwt:Audience"] = Audience,
            ["Authentication:Jwt:SigningKey"] = Convert.ToBase64String(SigningKey),
            ["Authentication:Demo:Enabled"] = demoEnabled.ToString(),
            ["Authentication:Demo:OrganizationEntityId"] = OrganizationId.ToString("D"),
            ["OpenApi:Enabled"] = openApiEnabled.ToString(),
            ["Jobs:Enabled"] = glJobsEnabled.ToString(),
            ["Claims:AllowDirectClosure"] = allowDirectClosure.ToString()
        };
        if (legacyHandlerId is not null)
        {
            settings[$"Authentication:Demo:Accounts:{UserRoles.Handler}:UserId"] = legacyHandlerId;
        }

        if (claimsConnection is not null)
        {
            settings["ConnectionStrings:Claims"] = claimsConnection;
        }

        // The API reads these settings before WebApplicationBuilder.Build().
        builder.UseEnvironment(environment);
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(settings));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            if (!useDatabaseUsers)
            {
                services.RemoveAll<IUserStore>();
                services.RemoveAll<IClaimHandlerDirectory>();
                var directory = new TestUserStore(OrganizationId, accounts, handlerDisplayName ?? "Handler");
                services.AddSingleton<IUserStore>(directory);
                services.AddSingleton<IClaimHandlerDirectory>(directory);
            }
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddControllers().AddApplicationPart(typeof(AuthorizationTestController).Assembly);
        });
    }

    private static string RandomSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    internal sealed record DemoAccount(Guid UserId, string Password);

    private sealed class TestUserStore(Guid organizationId, Dictionary<string, DemoAccount> accounts, string handlerName)
        : IUserStore, IClaimHandlerDirectory
    {
        private readonly PasswordHasher<string> hasher = new();

        public Task<StoredUser?> FindForLoginAsync(Guid organization, string name, CancellationToken cancellationToken)
        {
            var pair = accounts.FirstOrDefault(item => item.Key.ToUpperInvariant() == name);
            return Task.FromResult(organization == organizationId && pair.Value is not null
                ? new StoredUser(pair.Value.UserId, organizationId, pair.Key, pair.Key, hasher.HashPassword(pair.Key, pair.Value.Password))
                : null);
        }

        public Task<bool> IsHandlerAsync(Guid organization, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(organization == organizationId && accounts[UserRoles.Handler].UserId == userId);

        public Task<IReadOnlyList<Guid>> FindByNameAsync(Guid organization, string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(organization == organizationId && handlerName.Contains(name, StringComparison.OrdinalIgnoreCase)
                ? [accounts[UserRoles.Handler].UserId] : []);
    }
}
