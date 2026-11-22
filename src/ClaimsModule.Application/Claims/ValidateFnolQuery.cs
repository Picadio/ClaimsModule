using AutoMapper;
using ClaimsModule.Application.Reserves;
using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Reserves;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Claims;

public sealed record FnolValidationResult(IReadOnlyList<ClaimWarningDto> Warnings,
    ReserveApprovalStatus? InitialReserveApprovalStatus, ReserveApprovalAuthority? InitialReserveRequiredAuthority);

public sealed record ValidateFnolQuery(CreateClaimRequest Request) : IRequest<FnolValidationResult>;

internal sealed class ValidateFnolValidator : AbstractValidator<ValidateFnolQuery>
{
    public ValidateFnolValidator(TimeProvider clock) =>
        RuleFor(query => query.Request).SetValidator(new FnolRequestValidator(clock));
}

internal sealed class ClaimIntake(IClaimStore store)
{
    public async Task<Claim> PrepareAsync(CreateClaimRequest request, UserIdentity user, DateTimeOffset reportedAt,
        int sequence, CancellationToken cancellationToken)
    {
        var policy = request.PolicyId is Guid id ? await store.FindPolicyAsync(id, cancellationToken) : null;
        var cause = request.LossEvent.CauseOfLossCode.Trim().ToUpperInvariant();
        var activeCause = await store.IsCauseActiveAsync(cause, cancellationToken);
        return Claim.Report(user.OrganizationEntityId, user.UserId, reportedAt, sequence,
            new FnolDetails(request.ClaimType, request.LossEvent, request.Parties, request.RiskObjects ?? [],
                request.Severity ?? ClaimSeverity.Standard), policy, activeCause);
    }
}

internal sealed class ValidateFnolHandler(ClaimIntake intake, ICurrentUser user, IMapper mapper, TimeProvider clock)
    : IRequestHandler<ValidateFnolQuery, FnolValidationResult>
{
    public async Task<FnolValidationResult> Handle(ValidateFnolQuery query, CancellationToken cancellationToken)
    {
        var actor = user.Identity;
        var now = clock.GetUtcNow();
        var claim = await intake.PrepareAsync(query.Request, actor, now, sequence: 1, cancellationToken);
        var warnings = mapper.Map<List<ClaimWarningDto>>(claim.Warnings);

        ReserveHistory? reserve = null;
        if (query.Request.InitialReserve is { } initial)
        {
            claim.EnsureCanSubmitReserve();
            var component = new ClaimReserveComponent(claim.OrganizationEntityId, claim.Id, initial.Component!.Value,
                actor.UserId, now);
            reserve = component.Submit(ReserveWriter.InitialChange(initial.Amount), 0, false, actor.UserId, now);
            if (reserve.RequiresManagerOverride)
            {
                warnings.Add(new ClaimWarningDto(ReserveTransaction.LimitWarningCode, "InitialReserve.Amount",
                    ReserveTransaction.LimitWarningMessage));
            }
        }

        return new FnolValidationResult(warnings, reserve?.ApprovalStatus, reserve?.RequiredAuthority);
    }
}
