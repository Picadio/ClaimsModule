using ClaimsModule.Domain.Common;

namespace ClaimsModule.Persistence.Claims;

internal sealed class Policy : OrganizationRecord
{
    public string PolicyNumber { get; private set; } = null!;
    public string ClientName { get; private set; } = null!;
    public DateOnly EffectiveDate { get; private set; }
    public DateOnly ExpirationDate { get; private set; }
    public string Status { get; private set; } = null!;
    public string CoverageTypes { get; private set; } = null!;
}

internal sealed class CauseOfLoss : OrganizationRecord
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string PerilCategory { get; private set; } = null!;
    public bool IsActive { get; private set; }
}

internal sealed class ClaimNumberCounter : OrganizationRecord
{
    public int Year { get; private set; }
    public int LastIssued { get; private set; }

    private ClaimNumberCounter()
    {
    }

    public ClaimNumberCounter(Guid organizationId, int year, DateTimeOffset createdAt)
        : base(organizationId, null, createdAt)
    {
        Year = year;
    }

    public int Advance() => ++LastIssued;
}

internal sealed class ClaimSubmission : OrganizationRecord
{
    public Guid ClaimId { get; private set; }
    public string KeyHash { get; private set; } = null!;
    public string RequestHash { get; private set; } = null!;
    public string ResponseJson { get; private set; } = null!;

    private ClaimSubmission()
    {
    }

    public ClaimSubmission(Guid organizationId, Guid userId, DateTimeOffset createdAt, Guid claimId, string keyHash,
        string requestHash, string responseJson) : base(organizationId, userId, createdAt)
    {
        ClaimId = claimId;
        KeyHash = keyHash;
        RequestHash = requestHash;
        ResponseJson = responseJson;
    }
}

internal sealed class ClaimAuditEntry : OrganizationRecord
{
    public Guid ClaimId { get; private set; }
    public Guid CorrelationId { get; private set; }
    public string EventType { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public string? OldValues { get; private set; }
    public string? NewValues { get; private set; }
    public Guid? RelatedEntityId { get; private set; }
    public string? RelatedEntityType { get; private set; }
    public int Position { get; private set; }

    private ClaimAuditEntry()
    {
    }

    public ClaimAuditEntry(Guid organizationId, Guid? actorId, DateTimeOffset createdAt, Guid claimId, Guid correlationId,
        string eventType, string description, string? oldValues, string? newValues, Guid? relatedEntityId,
        string? relatedEntityType, int position)
        : base(organizationId, actorId, createdAt)
    {
        ClaimId = claimId;
        CorrelationId = correlationId;
        EventType = eventType;
        Description = description;
        OldValues = oldValues;
        NewValues = newValues;
        RelatedEntityId = relatedEntityId;
        RelatedEntityType = relatedEntityType;
        Position = position;
    }
}

internal sealed class ClaimStatusSubmission : OrganizationRecord
{
    public Guid ClaimId { get; private set; }
    public string KeyHash { get; private set; } = null!;
    public string RequestHash { get; private set; } = null!;
    public string ResponseJson { get; private set; } = null!;

    private ClaimStatusSubmission()
    {
    }

    public ClaimStatusSubmission(Guid organizationId, Guid userId, DateTimeOffset createdAt, Guid claimId,
        string keyHash, string requestHash, string responseJson) : base(organizationId, userId, createdAt)
    {
        ClaimId = claimId;
        KeyHash = keyHash;
        RequestHash = requestHash;
        ResponseJson = responseJson;
    }
}
