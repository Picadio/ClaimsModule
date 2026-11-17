using ClaimsModule.Application.Security;

namespace ClaimsModule.API.Security;

internal sealed class HttpCurrentUser(IHttpContextAccessor contextAccessor) : ICurrentUser
{
    public UserIdentity Identity =>
        UserClaims.Read(contextAccessor.HttpContext?.User)
        ?? throw new InvalidOperationException("The current request has no authenticated ClaimsModule user.");
}
