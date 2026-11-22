using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Claims;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Claims;

public sealed record ListClaimsQuery : IRequest<ClaimPage>
{
    public ClaimStatus[]? Status { get; init; }
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }
    public Guid? AssignedHandlerId { get; init; }
    public string? AssignedHandlerName { get; init; }
    public string? CauseOfLossCode { get; init; }
    public Guid? PolicyId { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record ClaimPage(IReadOnlyList<ClaimSummary> Items, int Page, int PageSize, int TotalCount);

public sealed record ClaimSummary(Guid Id, string ClaimNumber, string? ClientName, string? PolicyNumber,
    DateTimeOffset LossDate, string CauseOfLossCode, ClaimStatus Status, decimal TotalReserves, Guid? AssignedHandlerId)
{
    public bool IsSlaBreached { get; init; }
    public DateTimeOffset? LastSlaBreachAt { get; init; }
}

internal sealed class ListClaimsHandler(IClaimStore store, IClaimHandlerDirectory handlers, ICurrentUser currentUser)
    : IRequestHandler<ListClaimsQuery, ClaimPage>
{
    public async Task<ClaimPage> Handle(ListClaimsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid>? handlerIds = null;
        if (request.AssignedHandlerName?.Trim() is { Length: > 0 } name)
        {
            handlerIds = await handlers.FindByNameAsync(currentUser.Identity.OrganizationEntityId, name, cancellationToken);
            if (handlerIds.Count == 0 || (request.AssignedHandlerId.HasValue && !handlerIds.Contains(request.AssignedHandlerId.Value)))
            {
                return new ClaimPage([], request.Page, request.PageSize, 0);
            }
        }

        return await store.ListAsync(request, handlerIds, cancellationToken);
    }
}

internal sealed class ListClaimsValidator : AbstractValidator<ListClaimsQuery>
{
    public ListClaimsValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1)
            .Must((query, page) => (page - 1L) * query.PageSize <= int.MaxValue)
            .WithMessage("The requested page is too large.");
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleForEach(query => query.Status!).IsInEnum();
        RuleFor(query => query.DateTo).Must((query, dateTo) => !(dateTo < query.DateFrom))
            .WithMessage("DateTo must be on or after DateFrom.");
        RuleFor(query => query.AssignedHandlerName).MaximumLength(255);
        RuleFor(query => query.CauseOfLossCode).MaximumLength(50);
        RuleFor(query => query.Search).MaximumLength(255);
    }
}
