namespace ClaimsModule.Application.Security;

public sealed record StoredUser(Guid Id, Guid OrganizationId, string UserName, string Role, string PasswordHash);

public sealed record UserReference(Guid UserId, string DisplayName, string Role);

public sealed record NewDemoUser(Guid OrganizationId, string UserName, string NormalizedUserName,
    string Role, string PasswordHash, string? RegistrationKeyHash, DateTimeOffset CreatedAt);

public interface IUserPasswordHasher
{
    string Hash(string username, string password);
    bool Verify(string username, string passwordHash, string password);
}

public interface IUserStore
{
    Task<StoredUser?> FindForLoginAsync(Guid organizationId, string normalizedUserName, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserReference>> ListAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<StoredUser?> FindRegistrationAsync(Guid organizationId, string keyHash, CancellationToken cancellationToken);
    Task<Guid?> TryCreateAsync(NewDemoUser user, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, string>> FindDisplayNamesAsync(Guid organizationId, Guid[] userIds,
        CancellationToken cancellationToken);
}
