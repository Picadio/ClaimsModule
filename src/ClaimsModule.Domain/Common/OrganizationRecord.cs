namespace ClaimsModule.Domain.Common;

public abstract class OrganizationRecord
{
    public Guid Id { get; private set; }
    public Guid OrganizationEntityId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    
    protected OrganizationRecord()
    {
    }

    protected OrganizationRecord(Guid organizationEntityId, Guid? userId, DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(organizationEntityId, Guid.Empty);
        OrganizationEntityId = organizationEntityId;
        CreatedByUserId = userId;
        CreatedAt = createdAt.ToUniversalTime();
    }

    protected void MarkUpdated(Guid actorId, DateTimeOffset occurredAt)
    {
        UpdatedByUserId = actorId;
        UpdatedAt = occurredAt.ToUniversalTime();
    }

    protected void MarkDeleted(Guid actorId, DateTimeOffset occurredAt)
    {
        IsDeleted = true;
        DeletedAt = occurredAt.ToUniversalTime();
        MarkUpdated(actorId, occurredAt);
    }
}
