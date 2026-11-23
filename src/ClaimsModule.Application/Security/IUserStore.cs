namespace ClaimsModule.Application.Security;

public sealed record StoredUser(Guid Id, Guid OrganizationId, string UserName, string Role, string PasswordHash);

public sealed record UserReference(Guid UserId, string DisplayName, string Role);

public interface IUserStore
{
    Task<StoredUser?> FindForLoginAsync(Guid organizationId, string normalizedUserName, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserReference>> ListAsync(Guid organizationId, CancellationToken cancellationToken);
}
