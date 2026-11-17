using System.Security.Claims;
using ClaimsModule.Application.Security;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ClaimsModule.API.Security;

internal static class UserClaims
{
    public const string Organization = "org_id";
    public const string Role = "role";

    public static UserIdentity? Read(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        if (!Guid.TryParse(SingleValue(principal, JwtRegisteredClaimNames.Sub), out var userId) ||
            userId == Guid.Empty ||
            !Guid.TryParse(SingleValue(principal, Organization), out var organizationId) ||
            organizationId == Guid.Empty)
        {
            return null;
        }

        var role = SingleValue(principal, Role);
        return role is UserRoles.Handler or UserRoles.Supervisor or UserRoles.Manager
            ? new UserIdentity(userId, organizationId, role)
            : null;
    }

    private static string? SingleValue(ClaimsPrincipal principal, string type)
    {
        var claims = principal.FindAll(type).Take(2).ToArray();
        return claims.Length == 1 ? claims[0].Value : null;
    }
}
