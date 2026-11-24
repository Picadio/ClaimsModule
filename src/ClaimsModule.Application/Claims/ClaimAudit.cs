using System.Text.Json;
using ClaimsModule.Domain.Claims;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Claims;

public interface IAuditLogService
{
    void Append(ClaimAuditEvent entry);
}

public sealed record ClaimAuditEvent(Guid OrganizationId, Guid ClaimId, Guid? ActorId, DateTimeOffset OccurredAt, Guid CorrelationId,
    string EventType, string Description, object? OldValues = null, object? NewValues = null,
    Guid? RelatedEntityId = null, string? RelatedEntityType = null, int Position = 0)
{
    internal static ClaimAuditEvent PartyAdded(Claim claim, ClaimParty party, Guid? actor, DateTimeOffset now,
        Guid correlationId, int position = 0) => new(claim.OrganizationEntityId, claim.Id, actor, now, correlationId,
        "PARTY_ADDED", "A party was added to the claim.", NewValues: new { Role = party.Role.ToString(), party.IsActive },
        RelatedEntityId: party.Id, RelatedEntityType: nameof(ClaimParty), Position: position);
}

public interface IClaimAuditReader
{
    Task<ClaimAuditPage?> ReadPageAsync(Guid claimId, int page, int pageSize, CancellationToken cancellationToken);
}

public sealed record ClaimAuditItem(Guid Id, DateTimeOffset CreatedAt, Guid? ActorId, string EventType,
    string Description, JsonElement? OldValues, JsonElement? NewValues, Guid? RelatedEntityId,
    string? RelatedEntityType, Guid CorrelationId);

public sealed record ClaimAuditPage(IReadOnlyList<ClaimAuditItem> Items, int Page, int PageSize, int TotalCount);

public sealed record ListClaimAuditQuery : IRequest<ClaimAuditPage?>
{
    public Guid ClaimId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

internal sealed class ListClaimAuditHandler(IClaimAuditReader audit) : IRequestHandler<ListClaimAuditQuery, ClaimAuditPage?>
{
    public Task<ClaimAuditPage?> Handle(ListClaimAuditQuery query, CancellationToken cancellationToken) =>
        audit.ReadPageAsync(query.ClaimId, query.Page, query.PageSize, cancellationToken);
}

internal sealed class ListClaimAuditValidator : AbstractValidator<ListClaimAuditQuery>
{
    public ListClaimAuditValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1)
            .Must((query, page) => (page - 1L) * query.PageSize <= int.MaxValue)
            .WithMessage("The requested page is too large.");
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
    }
}
