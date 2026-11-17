using ClaimsModule.Application.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ClaimsModule.API.Security;

public sealed class DemoTokenIssuer
{
    private const int LifetimeSeconds = 30 * 60;

    private readonly Dictionary<string, DemoAccount> accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly PasswordHasher<string> passwords = new();
    private readonly JsonWebTokenHandler tokens = new();
    private readonly string issuer;
    private readonly string audience;
    private readonly SigningCredentials signingCredentials;

    internal DemoTokenIssuer(IConfigurationSection configuration, string issuer, string audience,
        SecurityKey signingKey)
    {
        this.issuer = issuer;
        this.audience = audience;
        signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        Enabled = configuration.GetValue<bool>("Enabled");

        if (!Enabled)
        {
            return;
        }

        var organizationId = ReadId(configuration, "OrganizationEntityId");
        var userIds = new HashSet<Guid>();
        foreach (var role in new[] { UserRoles.Handler, UserRoles.Supervisor, UserRoles.Manager })
        {
            var account = configuration.GetRequiredSection($"Accounts:{role}");
            var userId = ReadId(account, "UserId");
            if (!userIds.Add(userId))
            {
                throw new InvalidOperationException("Demo accounts must have distinct user IDs.");
            }

            if (account["Password"] is { } password && !string.IsNullOrWhiteSpace(password))
            {
                var identity = new UserIdentity(userId, organizationId, role);
                accounts.Add(role, new DemoAccount(identity, passwords.HashPassword(role, password)));
            }
        }
    }

    public bool Enabled { get; }

    public DemoTokenResponse? Issue(string username, string password)
    {
        if (!accounts.TryGetValue(username, out var account) ||
            passwords.VerifyHashedPassword(account.Identity.Role, account.PasswordHash, password)
            == PasswordVerificationResult.Failed)
        {
            return null;
        }

        var identity = account.Identity;
        var now = DateTime.UtcNow;
        var token = tokens.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddSeconds(LifetimeSeconds),
            SigningCredentials = signingCredentials,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = identity.UserId.ToString("D"),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("D"),
                [UserClaims.Organization] = identity.OrganizationEntityId.ToString("D"),
                [UserClaims.Role] = identity.Role
            }
        });

        return new DemoTokenResponse(token, "Bearer", LifetimeSeconds);
    }

    private static Guid ReadId(IConfigurationSection configuration, string name) =>
        Guid.TryParse(configuration[name], out var id) && id != Guid.Empty
            ? id
            : throw new InvalidOperationException($"{configuration.Path}:{name} must contain a nonempty GUID.");

    private sealed record DemoAccount(UserIdentity Identity, string PasswordHash);
}

public sealed record DemoTokenResponse(string AccessToken, string TokenType, int ExpiresIn);
