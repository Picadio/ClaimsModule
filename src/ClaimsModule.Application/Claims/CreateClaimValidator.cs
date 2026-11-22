using ClaimsModule.Application.Reserves;
using ClaimsModule.Domain.Claims;
using FluentValidation;

namespace ClaimsModule.Application.Claims;

internal sealed class CreateClaimValidator : AbstractValidator<CreateClaimCommand>
{
    public CreateClaimValidator(TimeProvider clock)
    {
        RuleFor(command => command.IdempotencyKey)
            .MaximumLength(128)
            .Must(key => key is null || !string.IsNullOrWhiteSpace(key))
            .WithMessage("Idempotency-Key cannot be blank.");
        RuleFor(command => command.Request).SetValidator(new FnolRequestValidator(clock));
    }
}

internal sealed class FnolRequestValidator : AbstractValidator<CreateClaimRequest>
{
    public FnolRequestValidator(TimeProvider clock)
    {
        RuleFor(request => request.PolicyId).NotEqual(Guid.Empty);
        RuleFor(request => request.ClaimType).NotEmpty().MaximumLength(50);
        RuleFor(request => request.Severity).IsInEnum();
        RuleFor(request => request.LossEvent).NotNull().SetValidator(new LossValidator(clock));
        RuleFor(request => request.Parties)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(parties => parties.Any(party => party is { Role: ClaimPartyRole.Claimant }))
            .WithMessage("At least one claimant is required.");
        RuleForEach(request => request.Parties).NotNull().SetValidator(new PartyValidator());
        RuleForEach(request => request.RiskObjects!).NotNull().SetValidator(new RiskValidator());
        RuleFor(request => request.InitialReserve!).SetValidator(new InitialReserveValidator());
    }

    private sealed class InitialReserveValidator : AbstractValidator<InitialReserveRequest>
    {
        public InitialReserveValidator()
        {
            RuleFor(reserve => reserve.Component).NotNull().IsInEnum();
            RuleFor(reserve => reserve.Amount).NotEqual(0m).PrecisionScale(19, 4, true);
        }
    }

    private sealed class LossValidator : AbstractValidator<LossDetails>
    {
        public LossValidator(TimeProvider clock)
        {
            RuleFor(loss => loss.LossDate).Cascade(CascadeMode.Stop).NotEmpty()
                .Must(date => date <= clock.GetUtcNow()).WithMessage("The loss date cannot be in the future.");
            RuleFor(loss => loss.CauseOfLossCode).NotEmpty().MaximumLength(50);
            RuleFor(loss => loss.LossDescription).Cascade(CascadeMode.Stop).NotEmpty()
                .Must(description => description.Trim().Length >= FnolRules.MinLossDescriptionLength)
                .WithMessage("The loss description must contain at least 20 characters.");
            RuleFor(loss => loss.LossLocation).MaximumLength(500);
            RuleFor(loss => loss.PoliceReportNumber).MaximumLength(100);
            RuleFor(loss => loss.EstimatedLossAmount).GreaterThanOrEqualTo(0).PrecisionScale(19, 4, true);
        }
    }

    private sealed class RiskValidator : AbstractValidator<RiskDetails>
    {
        public RiskValidator()
        {
            RuleFor(risk => risk.AssetType).IsInEnum();
            RuleFor(risk => risk.AssetDescription).NotEmpty().MaximumLength(500);
            RuleFor(risk => risk.AssetReference).MaximumLength(255);
        }
    }
}
