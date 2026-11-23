using ClaimsModule.Application.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ClaimsModule.API.Security;

public sealed class DemoTokenIssuer(IUserStore users, IUserPasswordHasher passwords, DemoTokenOptions options)
{
    private const int LifetimeSeconds = 30 * 60;
    private readonly JsonWebTokenHandler tokens = new();

    public bool Enabled => options.Enabled;

    public async Task<DemoTokenResponse?> IssueAsync(string username, string password, CancellationToken cancellationToken)
    {
        var user = await users.FindForLoginAsync(options.OrganizationId, username.Trim().ToUpperInvariant(), cancellationToken);
        if (user is null || !passwords.Verify(user.UserName, user.PasswordHash, password))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var token = tokens.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer, Audience = options.Audience, IssuedAt = now, NotBefore = now,
            Expires = now.AddSeconds(LifetimeSeconds), SigningCredentials = new SigningCredentials(options.SigningKey, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString("D"),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("D"),
                [UserClaims.Organization] = user.OrganizationId.ToString("D"),
                [UserClaims.Role] = user.Role
            }
        });
        return new DemoTokenResponse(token, "Bearer", LifetimeSeconds);
    }
}

public sealed record DemoTokenOptions(bool Enabled, bool AllowRegistration, Guid OrganizationId,
    string Issuer, string Audience, SecurityKey SigningKey);
public sealed record DemoTokenResponse(string AccessToken, string TokenType, int ExpiresIn);
