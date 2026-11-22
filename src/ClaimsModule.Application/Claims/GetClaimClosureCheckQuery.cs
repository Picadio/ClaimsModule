using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Claims;
using MediatR;

namespace ClaimsModule.Application.Claims;

public sealed record GetClaimClosureCheckQuery(Guid Id) : IRequest<ClaimClosureCheck?>;

public sealed record ClaimClosureCheck(bool TransitionAllowed, bool RequiresPositiveReserveConfirmation,
    IReadOnlyDictionary<string, string[]> Blockers)
{
    public bool CanClose => TransitionAllowed && Blockers.Count == 0;
}

internal sealed class GetClaimClosureCheckHandler(IClaimStore claims, IClaimLifecycleStore lifecycle,
    IClaimUnitOfWork unitOfWork, ICurrentUser currentUser, ClaimLifecycleOptions options, TimeProvider clock)
    : IRequestHandler<GetClaimClosureCheckQuery, ClaimClosureCheck?>
{
    public Task<ClaimClosureCheck?> Handle(GetClaimClosureCheckQuery request, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteClaimReadAsync<ClaimClosureCheck?>(request.Id, async token =>
        {
            var claim = await claims.FindAsync(request.Id, token);
            if (claim is null)
            {
                return null;
            }

            var rules = await lifecycle.ListTransitionsAsync(token);
            var transitionIssue = ClaimTransitionRule.CheckTransition(rules, currentUser.Identity.Role, options,
                claim.Status, ClaimStatus.Closed) ?? claim.GetStatusTransitionRestriction(ClaimStatus.Closed);
            var causeActive = claim.LossEvent is not null && await claims.IsCauseActiveAsync(claim.LossEvent.CauseOfLossCode, token);
            var reserves = await lifecycle.ReadReserveFactsAsync(claim.Id, token);
            var issues = claim.GetClosureIssues(new ClaimLifecycleFacts(causeActive, reserves), clock.GetUtcNow()).ToList();
            if (transitionIssue is not null)
            {
                issues.Insert(0, transitionIssue);
            }

            var blockers = issues.GroupBy(issue => issue.Field)
                .ToDictionary(group => group.Key, group => group.Select(issue => issue.Message).ToArray());
            return new ClaimClosureCheck(transitionIssue is null, reserves.HasPositiveComponentBalance, blockers);
        }, cancellationToken);
}
