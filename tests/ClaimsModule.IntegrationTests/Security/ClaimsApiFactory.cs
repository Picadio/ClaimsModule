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
    private readonly bool allowRegistration;
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
        bool useDatabaseUsers = false, bool allowRegistration = false)
    {
        this.demoEnabled = demoEnabled;
        this.environment = environment ?? Environments.Development;
        this.claimsConnection = claimsConnection;
        this.openApiEnabled = openApiEnabled;
        this.allowDirectClosure = allowDirectClosure;
        this.glJobsEnabled = glJobsEnabled;
        this.handlerDisplayName = handlerDisplayName;
        this.useDatabaseUsers = useDatabaseUsers;
        this.allowRegistration = allowRegistration;
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
            ["Authentication:Demo:AllowRegistration"] = allowRegistration.ToString(),
            ["Authentication:Demo:OrganizationEntityId"] = OrganizationId.ToString("D"),
            ["OpenApi:Enabled"] = openApiEnabled.ToString(),
            ["Jobs:Enabled"] = glJobsEnabled.ToString(),
            ["Claims:AllowDirectClosure"] = allowDirectClosure.ToString()
        };
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
        private readonly Dictionary<Guid, NewDemoUser> registered = new();

        public Task<StoredUser?> FindForLoginAsync(Guid organization, string name, CancellationToken cancellationToken)
        {
            lock (registered)
            {
                var registration = registered.FirstOrDefault(pair => pair.Value.OrganizationId == organization &&
                    pair.Value.NormalizedUserName == name);
                if (registration.Value is { } user)
                {
                    return Task.FromResult<StoredUser?>(new StoredUser(registration.Key, organization,
                        user.UserName, user.Role, user.PasswordHash));
                }
            }

            var pair = accounts.FirstOrDefault(item => item.Key.ToUpperInvariant() == name);
            return Task.FromResult(organization == organizationId && pair.Value is not null
                ? new StoredUser(pair.Value.UserId, organizationId, pair.Key, pair.Key, hasher.HashPassword(pair.Key, pair.Value.Password))
                : null);
        }

        public Task<StoredUser?> FindRegistrationAsync(Guid organization, string keyHash, CancellationToken cancellationToken)
        {
            lock (registered)
            {
                var match = registered.FirstOrDefault(pair => pair.Value.OrganizationId == organization &&
                    pair.Value.RegistrationKeyHash == keyHash);
                return Task.FromResult(match.Value is { } user
                    ? new StoredUser(match.Key, organization, user.UserName, user.Role, user.PasswordHash)
                    : null);
            }
        }

        public Task<Guid?> TryCreateAsync(NewDemoUser user, CancellationToken cancellationToken)
        {
            lock (registered)
            {
                if (registered.Values.Any(other => other.OrganizationId == user.OrganizationId &&
                    (other.NormalizedUserName == user.NormalizedUserName ||
                     user.RegistrationKeyHash is not null && other.RegistrationKeyHash == user.RegistrationKeyHash)))
                {
                    return Task.FromResult<Guid?>(null);
                }

                var id = Guid.NewGuid();
                registered.Add(id, user);
                return Task.FromResult<Guid?>(id);
            }
        }

        public async Task<IReadOnlyDictionary<Guid, string>> FindDisplayNamesAsync(Guid organization, Guid[] userIds,
            CancellationToken cancellationToken) =>
            (await ListAsync(organization, cancellationToken)).Where(user => userIds.Contains(user.UserId))
                .ToDictionary(user => user.UserId, user => user.DisplayName);

        public Task<bool> IsHandlerAsync(Guid organization, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(organization == organizationId && accounts[UserRoles.Handler].UserId == userId);

        public Task<IReadOnlyList<UserReference>> ListAsync(Guid organization, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserReference>>(organization == organizationId
                ? accounts.Select(pair => new UserReference(pair.Value.UserId,
                    pair.Key == UserRoles.Handler ? handlerName : pair.Key, pair.Key)).ToArray()
                : []);

        public Task<IReadOnlyList<Guid>> FindByNameAsync(Guid organization, string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(organization == organizationId && handlerName.Contains(name, StringComparison.OrdinalIgnoreCase)
                ? [accounts[UserRoles.Handler].UserId] : []);
    }
}
