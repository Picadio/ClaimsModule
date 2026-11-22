using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Security;

namespace ClaimsModule.API.Security;

internal sealed class DemoClaimHandlerDirectory(IConfiguration configuration) : IClaimHandlerDirectory
{
    public bool IsHandler(Guid organizationId, Guid userId) => GetHandlerId(organizationId) == userId;

    public Guid? FindByName(Guid organizationId, string name)
    {
        var configuredName = configuration[$"Authentication:Demo:Accounts:{UserRoles.Handler}:DisplayName"];
        var displayName = string.IsNullOrWhiteSpace(configuredName) ? "Handler" : configuredName.Trim();
        return displayName.Contains(name, StringComparison.OrdinalIgnoreCase) ? GetHandlerId(organizationId) : null;
    }

    private Guid? GetHandlerId(Guid organizationId) =>
        organizationId != Guid.Empty &&
        Guid.TryParse(configuration["Authentication:Demo:OrganizationEntityId"], out var configuredOrganization) &&
        configuredOrganization == organizationId &&
        Guid.TryParse(configuration[$"Authentication:Demo:Accounts:{UserRoles.Handler}:UserId"], out var configuredHandler) &&
        configuredHandler != Guid.Empty ? configuredHandler : null;
}
