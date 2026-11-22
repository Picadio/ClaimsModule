using System.Security.Cryptography;
using System.Threading.RateLimiting;
using ClaimsModule.Application.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace ClaimsModule.API.Security;

internal static class AuthenticationExtensions
{
    public const string DemoLoginRateLimit = "DemoLogin";

    public static void AddClaimsAuthentication(this WebApplicationBuilder builder)
    {
        var jwt = builder.Configuration.GetRequiredSection("Authentication:Jwt");
        var issuer = RequiredValue(jwt, "Issuer");
        var audience = RequiredValue(jwt, "Audience");
        var signingKey = new SymmetricSecurityKey(ReadSigningKey(jwt, builder.Environment.IsDevelopment()));

        var services = builder.Services;
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        var demo = builder.Configuration.GetRequiredSection("Authentication:Demo");
        if (demo.GetValue<bool>("Enabled"))
        {
            ValidateLegacyUserIds(demo);
        }
        services.AddSingleton(new DemoTokenOptions(demo.GetValue<bool>("Enabled"),
            demo.GetValue<Guid>("OrganizationEntityId"), issuer, audience, signingKey));
        services.AddScoped<DemoTokenIssuer>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.SaveToken = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = signingKey,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    IgnoreTrailingSlashWhenValidatingAudience = false,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    RoleClaimType = UserClaims.Role,
                    ClockSkew = TimeSpan.Zero
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (UserClaims.Read(context.Principal) is null)
                        {
                            context.Fail("The token has no valid ClaimsModule user identity.");
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(ClaimsAuthorizationPolicies.SupervisorOrManager,
                policy => policy.RequireAuthenticatedUser().RequireRole(UserRoles.Supervisor, UserRoles.Manager))
            .AddPolicy(ClaimsAuthorizationPolicies.ManagerOnly,
                policy => policy.RequireAuthenticatedUser().RequireRole(UserRoles.Manager));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(DemoLoginRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1)
                }));
        });
    }

    private static void ValidateLegacyUserIds(IConfigurationSection demo)
    {
        var seedIds = new Dictionary<string, Guid>
        {
            [UserRoles.Handler] = Guid.Parse("76352da5-cc3f-49c0-8caf-149e6f51c4ec"),
            [UserRoles.Supervisor] = Guid.Parse("c274bc9c-934e-4910-a759-f1afbef3e46f"),
            [UserRoles.Manager] = Guid.Parse("a257d183-69ee-494a-84c3-5ce2215a26ab")
        };
        foreach (var (role, seedId) in seedIds)
        {
            var configured = demo[$"Accounts:{role}:UserId"];
            if (configured is not null && (!Guid.TryParse(configured, out var id) || id != seedId))
            {
                throw new InvalidOperationException("Legacy demo user IDs differ from seeded IDs. Migrate user identities before enabling demo login. See README.");
            }
        }
    }

    private static string RequiredValue(IConfigurationSection section, string name) =>
        section[name] is { } value && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"{section.Path}:{name} must be configured.");

    private static byte[] ReadSigningKey(IConfigurationSection jwt, bool development)
    {
        var configuredKey = jwt["SigningKey"];
        if (string.IsNullOrWhiteSpace(configuredKey) && development)
        {
            return RandomNumberGenerator.GetBytes(32);
        }

        Span<byte> key = new byte[configuredKey?.Length ?? 0];
        if (configuredKey is null ||
            !Convert.TryFromBase64String(configuredKey, key, out var bytesWritten) ||
            bytesWritten < 32)
        {
            throw new InvalidOperationException(
                "Authentication:Jwt:SigningKey must be a Base64 value containing at least 32 random bytes.");
        }

        return key[..bytesWritten].ToArray();
    }
}
