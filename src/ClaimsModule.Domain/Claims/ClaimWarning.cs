using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Claims;

public sealed class ClaimWarning : OrganizationRecord
{
    public Guid ClaimId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Field { get; private set; } = null!;
    public string Message { get; private set; } = null!;
    
    private ClaimWarning()
    {
    }

    internal ClaimWarning(Guid organizationId, Guid? actorId, DateTimeOffset createdAt, string code, string field,
        string message) : base(organizationId, actorId, createdAt)
    {
        Code = code;
        Field = field;
        Message = message;
    }
}
