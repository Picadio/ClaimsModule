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
        services.AddSingleton(new DemoTokenIssuer(
            builder.Configuration.GetRequiredSection("Authentication:Demo"), issuer, audience, signingKey));

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
