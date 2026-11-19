using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Reference;

public sealed record SearchPoliciesQuery(string? Q) : IRequest<IReadOnlyList<PolicySearchResult>>;

public sealed record GetPolicyCoverageQuery(Guid PolicyId) : IRequest<PolicyCoverage?>;

public sealed record ListCauseOfLossCodesQuery(string? PerilCategory) : IRequest<IReadOnlyList<CauseOfLossCode>>;

public sealed record PolicySearchResult(Guid PolicyId, string PolicyNumber, string ClientName,
    DateOnly EffectiveDate, DateOnly ExpirationDate, string Status, IReadOnlyList<string> CoverageTypes);

public sealed record PolicyCoverage(Guid PolicyId, IReadOnlyList<string> CoverageTypes);

public sealed record CauseOfLossCode(string Code, string Name, string PerilCategory);

public interface IReferenceDataStore
{
    Task<IReadOnlyList<PolicySearchResult>> SearchPoliciesAsync(string? search, CancellationToken cancellationToken);
    Task<PolicyCoverage?> FindPolicyCoverageAsync(Guid policyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CauseOfLossCode>> ListCauseOfLossCodesAsync(string? perilCategory,
        CancellationToken cancellationToken);
}

internal sealed class SearchPoliciesHandler(IReferenceDataStore store)
    : IRequestHandler<SearchPoliciesQuery, IReadOnlyList<PolicySearchResult>>
{
    public Task<IReadOnlyList<PolicySearchResult>> Handle(SearchPoliciesQuery request,
        CancellationToken cancellationToken) => store.SearchPoliciesAsync(request.Q, cancellationToken);
}

internal sealed class GetPolicyCoverageHandler(IReferenceDataStore store)
    : IRequestHandler<GetPolicyCoverageQuery, PolicyCoverage?>
{
    public Task<PolicyCoverage?> Handle(GetPolicyCoverageQuery request, CancellationToken cancellationToken) =>
        store.FindPolicyCoverageAsync(request.PolicyId, cancellationToken);
}

internal sealed class ListCauseOfLossCodesHandler(IReferenceDataStore store)
    : IRequestHandler<ListCauseOfLossCodesQuery, IReadOnlyList<CauseOfLossCode>>
{
    public Task<IReadOnlyList<CauseOfLossCode>> Handle(ListCauseOfLossCodesQuery request,
        CancellationToken cancellationToken) => store.ListCauseOfLossCodesAsync(request.PerilCategory, cancellationToken);
}

internal sealed class SearchPoliciesValidator : AbstractValidator<SearchPoliciesQuery>
{
    public SearchPoliciesValidator()
    {
        RuleFor(query => query.Q).MaximumLength(255);
    }
}

internal sealed class ListCauseOfLossCodesValidator : AbstractValidator<ListCauseOfLossCodesQuery>
{
    public ListCauseOfLossCodesValidator()
    {
        RuleFor(query => query.PerilCategory).MaximumLength(50);
    }
}
