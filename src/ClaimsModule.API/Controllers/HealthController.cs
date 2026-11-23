using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsModule.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api")]
public class HealthController : ControllerBase
{
    [HttpGet("health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<object>> Health()
    {
        return Ok(new {status = "ok"});
    }
}