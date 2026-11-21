using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Claims;
using MediatR;

namespace ClaimsModule.Application.Reference;

public sealed record ClaimStatusReference(ClaimStatus Status, IReadOnlyList<ClaimStatus> AllowedTransitions);

public sealed record ListClaimStatusesQuery : IRequest<IReadOnlyList<ClaimStatusReference>>;

internal sealed class ListClaimStatusesHandler(IClaimLifecycleStore lifecycle, ICurrentUser currentUser,
    ClaimLifecycleOptions options) : IRequestHandler<ListClaimStatusesQuery, IReadOnlyList<ClaimStatusReference>>
{
    public async Task<IReadOnlyList<ClaimStatusReference>> Handle(ListClaimStatusesQuery request,
        CancellationToken cancellationToken)
    {
        var rules = await lifecycle.ListTransitionsAsync(cancellationToken);
        var role = currentUser.Identity.Role;
        var available = ClaimTransitionRule.AvailableTo(rules, role, options).ToLookup(rule => rule.FromStatus);
        return Enum.GetValues<ClaimStatus>()
            .Select(status => new ClaimStatusReference(status,
                available[status].Select(rule => rule.ToStatus).Order().ToArray()))
            .ToArray();
    }
}
