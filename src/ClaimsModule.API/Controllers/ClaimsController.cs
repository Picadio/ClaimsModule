using System.Diagnostics;
using ClaimsModule.Application.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsModule.API.Controllers;

[Authorize]
[ApiController]
[Route("api/claims")]
public sealed class ClaimsController(ISender sender) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CreatedClaim>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedClaim>> Create(CreateClaimRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        var correlationId = Guid.TryParseExact(Activity.Current?.TraceId.ToString(), "N", out var traceId)
            ? traceId : Guid.NewGuid();
        Response.Headers["X-Correlation-ID"] = correlationId.ToString("D");
        var claim = await sender.Send(new CreateClaimCommand(request, idempotencyKey, correlationId), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = claim.Id }, claim);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ClaimDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClaimDetail>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var claim = await sender.Send(new GetClaimQuery(id), cancellationToken);
        return claim is null ? NotFound() : Ok(claim);
    }
}
