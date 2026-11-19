using System.Security.Cryptography;
using ClaimsModule.API.Controllers;
using ClaimsModule.Application.Security;
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
    private readonly Dictionary<string, DemoAccount> accounts =
        new[] { UserRoles.Handler, UserRoles.Supervisor, UserRoles.Manager }
            .ToDictionary(role => role, _ => new DemoAccount(Guid.NewGuid(), RandomSecret()));

    internal byte[] SigningKey { get; } = RandomNumberGenerator.GetBytes(32);
    internal Guid OrganizationId { get; }

    public ClaimsApiFactory() : this(demoEnabled: true)
    {
    }

    internal ClaimsApiFactory(bool demoEnabled, string? environment = null,
        string? claimsConnection = null, Guid? organizationId = null, bool openApiEnabled = false)
    {
        this.demoEnabled = demoEnabled;
        this.environment = environment ?? Environments.Development;
        this.claimsConnection = claimsConnection;
        this.openApiEnabled = openApiEnabled;
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
            ["OpenApi:Enabled"] = openApiEnabled.ToString()
        };
        foreach (var (role, account) in accounts)
        {
            settings[$"Authentication:Demo:Accounts:{role}:UserId"] = account.UserId.ToString("D");
            settings[$"Authentication:Demo:Accounts:{role}:Password"] = account.Password;
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
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddControllers().AddApplicationPart(typeof(AuthorizationTestController).Assembly);
        });
    }

    private static string RandomSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    internal sealed record DemoAccount(Guid UserId, string Password);
}
