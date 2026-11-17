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
    private readonly Dictionary<string, DemoAccount> accounts =
        new[] { UserRoles.Handler, UserRoles.Supervisor, UserRoles.Manager }
            .ToDictionary(role => role, _ => new DemoAccount(Guid.NewGuid(), RandomSecret()));

    public ClaimsApiFactory() : this(demoEnabled: true)
    {
    }

    internal ClaimsApiFactory(bool demoEnabled)
    {
        this.demoEnabled = demoEnabled;
    }

    internal byte[] SigningKey { get; } = RandomNumberGenerator.GetBytes(32);
    internal Guid OrganizationId { get; } = Guid.NewGuid();

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
            ["Authentication:Demo:OrganizationEntityId"] = OrganizationId.ToString("D")
        };
        foreach (var (role, account) in accounts)
        {
            settings[$"Authentication:Demo:Accounts:{role}:UserId"] = account.UserId.ToString("D");
            settings[$"Authentication:Demo:Accounts:{role}:Password"] = account.Password;
        }

        // The API reads these settings before WebApplicationBuilder.Build().
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
