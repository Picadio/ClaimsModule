namespace ClaimsModule.Application.Security;

public sealed record StoredUser(Guid Id, Guid OrganizationId, string UserName, string Role, string PasswordHash);

public interface IUserStore
{
    Task<StoredUser?> FindForLoginAsync(Guid organizationId, string normalizedUserName, CancellationToken cancellationToken);
}
