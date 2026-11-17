using System.ComponentModel.DataAnnotations;
using ClaimsModule.API.Security;
using ClaimsModule.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ClaimsModule.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(DemoTokenIssuer demoTokens, ICurrentUser currentUser) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationExtensions.DemoLoginRateLimit)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpPost("demo-token")]
    public ActionResult<DemoTokenResponse> IssueDemoToken(DemoTokenRequest request)
    {
        if (!demoTokens.Enabled)
        {
            return NotFound();
        }

        var token = demoTokens.Issue(request.Username, request.Password);
        return token is null ? Unauthorized() : Ok(token);
    }

    [Authorize]
    [HttpGet("me")]
    public ActionResult<UserIdentity> GetCurrentUser() => Ok(currentUser.Identity);
}

public sealed class DemoTokenRequest
{
    [Required, StringLength(64)]
    public string Username { get; init; } = string.Empty;

    [Required, StringLength(256)]
    public string Password { get; init; } = string.Empty;
}
