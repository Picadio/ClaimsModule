namespace ClaimsModule.Application.Security;

public interface ICurrentUser
{
    UserIdentity Identity { get; }
}

public sealed record UserIdentity(Guid UserId, Guid OrganizationEntityId, string Role);
