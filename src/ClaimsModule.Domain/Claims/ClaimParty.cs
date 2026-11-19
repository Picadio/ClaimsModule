using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Claims;

public sealed class ClaimParty : OrganizationRecord
{
    public Guid ClaimId { get; private set; }
    public ClaimPartyRole Role { get; private set; }
    public ClaimPartyType PartyType { get; private set; }
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? CompanyName { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }
    
    private ClaimParty()
    {
    }

    internal ClaimParty(Guid organizationId, Guid actorId, DateTimeOffset reportedAt, PartyDetails details)
        : base(organizationId, actorId, reportedAt)
    {
        Role = details.Role;
        PartyType = details.PartyType;
        FirstName = details.FirstName?.Trim();
        LastName = details.LastName?.Trim();
        CompanyName = details.CompanyName?.Trim();
        Email = details.Email?.Trim();
        Phone = details.Phone?.Trim();
        Notes = details.Notes?.Trim();
        IsActive = true;
    }
}
