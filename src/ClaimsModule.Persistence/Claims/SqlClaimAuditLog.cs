using System.Data;
using System.Text.Json;
using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Claims;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Claims;

internal sealed class SqlClaimAuditLog(ClaimsDbContext db) : IAuditLogService, IClaimAuditReader
{
    public void Append(ClaimAuditEvent entry) => db.Add(new ClaimAuditEntry(entry.OrganizationId, entry.ActorId,
        entry.OccurredAt, entry.ClaimId, entry.CorrelationId, entry.EventType, entry.Description,
        Serialize(entry.OldValues), Serialize(entry.NewValues), entry.RelatedEntityId, entry.RelatedEntityType,
        entry.Position));

    public async Task<ClaimAuditPage?> ReadPageAsync(Guid claimId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var visibleClaims = db.Set<Claim>().Where(claim => claim.Id == claimId);
        if (!await visibleClaims.AnyAsync(cancellationToken))
        {
            return null;
        }

        var audit = db.Set<ClaimAuditEntry>().AsNoTracking()
            .Where(entry => entry.ClaimId == claimId && visibleClaims.Any(claim => claim.Id == entry.ClaimId));
        var total = await audit.CountAsync(cancellationToken);
        var rows = await audit.OrderByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.Position)
            .ThenByDescending(entry => entry.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var items = rows.Select(entry => new ClaimAuditItem(entry.Id, entry.CreatedAt, entry.CreatedByUserId,
            entry.EventType, entry.Description, Deserialize(entry.OldValues), Deserialize(entry.NewValues),
            entry.RelatedEntityId, entry.RelatedEntityType, entry.CorrelationId)).ToArray();
        return new ClaimAuditPage(items, page, pageSize, total);
    }

    private static string? Serialize(object? value) => value is null ? null : JsonSerializer.Serialize(value);

    private static JsonElement? Deserialize(string? value) => value is null
        ? null
        : JsonSerializer.Deserialize<JsonElement>(value);
}
