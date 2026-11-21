using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Security;

namespace ClaimsModule.API.Security;

internal sealed class DemoClaimHandlerDirectory(IConfiguration configuration) : IClaimHandlerDirectory
{
    public bool IsHandler(Guid organizationId, Guid userId) =>
        Guid.TryParse(configuration["Authentication:Demo:OrganizationEntityId"], out var configuredOrganization)
        && configuredOrganization == organizationId
        && Guid.TryParse(configuration[$"Authentication:Demo:Accounts:{UserRoles.Handler}:UserId"], out var configuredHandler)
        && configuredHandler == userId;
}
