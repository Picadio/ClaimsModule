using System.ComponentModel.DataAnnotations;
using ClaimsModule.API.Security;
using ClaimsModule.Application.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ClaimsModule.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(DemoTokenIssuer demoTokens, DemoTokenOptions demo, ICurrentUser currentUser, ISender sender)
    : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationExtensions.DemoLoginRateLimit)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpPost("register")]
    [ProducesResponseType<UserReference>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UserReference>> Register(RegisterDemoUserRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (!demo.Enabled || !demo.AllowRegistration)
        {
            return NotFound();
        }

        var user = await sender.Send(new RegisterDemoUserCommand(demo.OrganizationId, request, idempotencyKey), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, user);
    }

    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationExtensions.DemoLoginRateLimit)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpPost("demo-token")]
    public async Task<ActionResult<DemoTokenResponse>> IssueDemoToken(DemoTokenRequest request, CancellationToken cancellationToken)
    {
        if (!demoTokens.Enabled)
        {
            return NotFound();
        }

        var token = await demoTokens.IssueAsync(request.Username, request.Password, cancellationToken);
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
