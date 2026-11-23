using ClaimsModule.Application.Security;
using MediatR;

namespace ClaimsModule.Application.Reference;

public sealed record ListUsersQuery : IRequest<IReadOnlyList<UserReference>>;

internal sealed class ListUsersHandler(IUserStore users, ICurrentUser currentUser)
    : IRequestHandler<ListUsersQuery, IReadOnlyList<UserReference>>
{
    public Task<IReadOnlyList<UserReference>> Handle(ListUsersQuery request, CancellationToken cancellationToken) =>
        users.ListAsync(currentUser.Identity.OrganizationEntityId, cancellationToken);
}
