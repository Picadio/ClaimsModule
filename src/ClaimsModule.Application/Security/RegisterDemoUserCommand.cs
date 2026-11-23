using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Exceptions;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Security;

public sealed record RegisterDemoUserRequest(string Username, string Password, string Role);

public sealed record RegisterDemoUserCommand(Guid OrganizationId, RegisterDemoUserRequest Request, string? IdempotencyKey)
    : IRequest<UserReference>;

internal sealed class RegisterDemoUserValidator : AbstractValidator<RegisterDemoUserCommand>
{
    public RegisterDemoUserValidator()
    {
        RuleFor(command => command.Request.Username).Cascade(CascadeMode.Stop).NotEmpty().MaximumLength(64)
            .Must(name => name.Trim().All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
            .WithMessage("Use only letters A-Z, digits, dots, underscores or hyphens.");
        RuleFor(command => command.Request.Password).NotEmpty().Length(8, 256);
        RuleFor(command => command.Request.Role)
            .Must(role => role is UserRoles.Handler or UserRoles.Supervisor or UserRoles.Manager)
            .WithMessage("Select handler, supervisor or manager.");
        RuleFor(command => command.IdempotencyKey).OptionalKey();
    }
}

internal sealed class RegisterDemoUserHandler(IUserStore users, IUserPasswordHasher passwords, TimeProvider clock)
    : IRequestHandler<RegisterDemoUserCommand, UserReference>
{
    public async Task<UserReference> Handle(RegisterDemoUserCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var username = request.Username.Trim();
        var key = IdempotencyFingerprint.HashKey(command.IdempotencyKey);
        var existing = await ReadRegistrationAsync(command.OrganizationId, key, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, username, request);
        }

        var user = new NewDemoUser(command.OrganizationId, username, username.ToUpperInvariant(), request.Role,
            passwords.Hash(username, request.Password), key, clock.GetUtcNow());
        if (await users.TryCreateAsync(user, cancellationToken) is { } id)
        {
            return new UserReference(id, username, request.Role);
        }

        existing = await ReadRegistrationAsync(command.OrganizationId, key, cancellationToken);
        return existing is not null ? Replay(existing, username, request) : throw new UsernameUnavailableException();
    }

    private Task<StoredUser?> ReadRegistrationAsync(Guid organizationId, string? key, CancellationToken cancellationToken) =>
        key is null ? Task.FromResult<StoredUser?>(null) : users.FindRegistrationAsync(organizationId, key, cancellationToken);

    private UserReference Replay(StoredUser user, string username, RegisterDemoUserRequest request) =>
        user.UserName == username && user.Role == request.Role &&
        passwords.Verify(user.UserName, user.PasswordHash, request.Password)
            ? new UserReference(user.Id, user.UserName, user.Role)
            : throw new IdempotencyConflictException();
}
