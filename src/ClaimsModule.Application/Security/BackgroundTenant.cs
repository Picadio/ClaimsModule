namespace ClaimsModule.Application.Security;

public sealed class BackgroundTenant
{
    public Guid? OrganizationId { get; private set; }

    public void Initialize(Guid organizationId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);
        if (OrganizationId.HasValue)
        {
            throw new InvalidOperationException("The background tenant has already been initialized.");
        }

        OrganizationId = organizationId;
    }
}
