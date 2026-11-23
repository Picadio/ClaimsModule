using ClaimsModule.Application.Security;
using Microsoft.AspNetCore.Identity;

namespace ClaimsModule.API.Security;

internal sealed class DemoPasswordHasher : IUserPasswordHasher
{
    private readonly PasswordHasher<string> passwords = new();

    public string Hash(string username, string password) => passwords.HashPassword(username, password);

    public bool Verify(string username, string passwordHash, string password) =>
        passwords.VerifyHashedPassword(username, passwordHash, password) != PasswordVerificationResult.Failed;
}
