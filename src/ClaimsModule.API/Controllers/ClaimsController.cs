using ClaimsModule.API.Correlation;
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
    [HttpGet]
    [ProducesResponseType<ClaimPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<ClaimPage> List([FromQuery] ListClaimsQuery query, CancellationToken cancellationToken) =>
        sender.Send(query, cancellationToken);

    [HttpPost]
    [ProducesResponseType<CreatedClaim>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedClaim>> Create(CreateClaimRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        var correlationId = RequestCorrelation.Read(HttpContext);
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

    [HttpPut("{id:guid}/status")]
    [ProducesResponseType<ClaimStatusResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClaimStatusResult>> ChangeStatus(Guid id, ChangeClaimStatusRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        var claim = await sender.Send(new ChangeClaimStatusCommand(id, request, idempotencyKey,
            RequestCorrelation.Read(HttpContext)), cancellationToken);
        return claim is null ? NotFound() : Ok(claim);
    }

    [HttpGet("{id:guid}/audit")]
    [ProducesResponseType<ClaimAuditPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ClaimAuditPage>> GetAudit(Guid id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var query = new ListClaimAuditQuery { ClaimId = id, Page = page, PageSize = pageSize };
        var audit = await sender.Send(query, cancellationToken);
        return audit is null ? NotFound() : Ok(audit);
    }
}
