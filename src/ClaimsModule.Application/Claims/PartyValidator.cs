using ClaimsModule.Domain.Claims;
using FluentValidation;

namespace ClaimsModule.Application.Claims;

internal sealed class PartyValidator : AbstractValidator<PartyDetails>
{
    public PartyValidator()
    {
        RuleFor(party => party.Role).IsInEnum();
        RuleFor(party => party.PartyType).IsInEnum();
        RuleFor(party => party.FirstName).MaximumLength(100);
        RuleFor(party => party.LastName).MaximumLength(100);
        RuleFor(party => party.CompanyName).MaximumLength(255);
        RuleFor(party => party.Email).MaximumLength(255);
        RuleFor(party => party.Phone).MaximumLength(50);
    }
}
