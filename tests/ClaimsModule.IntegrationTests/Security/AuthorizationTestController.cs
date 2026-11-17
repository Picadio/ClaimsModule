using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsModule.IntegrationTests.Security;

[Route("test/authorization")]
public sealed class AuthorizationTestController : ControllerBase
{
    [HttpGet("supervisor")]
    [Authorize(Policy = "SupervisorOrManager")]
    public IActionResult Supervisor() => NoContent();

    [HttpGet("manager")]
    [Authorize(Policy = "ManagerOnly")]
    public IActionResult Manager() => NoContent();
}
