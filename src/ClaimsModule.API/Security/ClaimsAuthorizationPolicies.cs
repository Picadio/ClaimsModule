namespace ClaimsModule.API.Security;

internal static class ClaimsAuthorizationPolicies
{
    public const string SupervisorOrManager = nameof(SupervisorOrManager);
    public const string ManagerOnly = nameof(ManagerOnly);
}
