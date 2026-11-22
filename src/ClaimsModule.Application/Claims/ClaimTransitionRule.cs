using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Exceptions;

namespace ClaimsModule.Application.Claims;

public sealed record ClaimTransitionRule(ClaimStatus FromStatus, ClaimStatus ToStatus, string RequiredPermission)
{
    public const string SystemOnly = "system";

    internal static ClaimRuleError? CheckTransition(IReadOnlyList<ClaimTransitionRule> rules, string actorRole,
        ClaimLifecycleOptions options, ClaimStatus current, ClaimStatus target)
    {
        var available = AvailableTo(rules, actorRole, options).Where(rule => rule.FromStatus == current)
            .Select(rule => rule.ToStatus).Order().ToArray();
        if (available.Contains(target))
        {
            return null;
        }

        var next = available.Length == 0 ? "No next statuses are available."
            : $"Available next statuses are {string.Join(", ", available)}.";
        return new("TargetStatus", $"The transition from {current} to {target} is not allowed for this request. {next}");
    }

    internal static IEnumerable<ClaimTransitionRule> AvailableTo(IReadOnlyList<ClaimTransitionRule> rules,
        string actorRole, ClaimLifecycleOptions options)
    {
        var reopenCompletes = rules.Any(rule => rule is
        {
            FromStatus: ClaimStatus.Reopened,
            ToStatus: ClaimStatus.Open,
            RequiredPermission: SystemOnly
        });

        return rules.Where(rule => (rule.ToStatus != ClaimStatus.Reopened || reopenCompletes) &&
            rule.CanApply(actorRole, options));
    }

    private bool CanApply(string actorRole, ClaimLifecycleOptions options)
    {
        if (FromStatus == ClaimStatus.Open && ToStatus == ClaimStatus.Closed && !options.AllowDirectClosure)
        {
            return false;
        }

        return RequiredPermission switch
        {
            UserRoles.Handler => actorRole is UserRoles.Handler or UserRoles.Supervisor or UserRoles.Manager,
            UserRoles.Supervisor => actorRole is UserRoles.Supervisor or UserRoles.Manager,
            UserRoles.Manager => actorRole is UserRoles.Manager,
            _ => false
        };
    }
}
