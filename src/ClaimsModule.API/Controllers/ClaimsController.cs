using ClaimsModule.API.Correlation;
using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Claims;
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

    [HttpPost("validate")]
    [ProducesResponseType<FnolValidationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<FnolValidationResult> ValidateFnol(CreateClaimRequest request, CancellationToken cancellationToken) =>
        sender.Send(new ValidateFnolQuery(request), cancellationToken);

    [HttpGet("{id:guid}")]
    [ResponseCache(NoStore = true)]
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

    [HttpPost("{id:guid}/parties")]
    [ProducesResponseType<ClaimPartyDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClaimPartyDto>> AddParty(Guid id, PartyDetails request,
        [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new AddClaimPartyCommand(id, request, key,
            RequestCorrelation.Read(HttpContext)), cancellationToken);
        return result is null ? NotFound() : CreatedAtAction(nameof(GetById), new { id }, result);
    }

    [HttpDelete("{id:guid}/parties/{partyId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveParty(Guid id, Guid partyId,
        [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RemoveClaimPartyCommand(id, partyId, key,
            RequestCorrelation.Read(HttpContext)), cancellationToken);
        return result is null ? NotFound() : NoContent();
    }

    [HttpPut("{id:guid}/notes")]
    [ProducesResponseType<ClaimNotesResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClaimNotesResult>> UpdateNotes(Guid id, UpdateClaimNotesRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new UpdateClaimNotesCommand(id, request, key,
            RequestCorrelation.Read(HttpContext)), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("{id:guid}/policy")]
    [ProducesResponseType<ClaimPolicyResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClaimPolicyResult>> LinkPolicy(Guid id, LinkClaimPolicyRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new LinkClaimPolicyCommand(id, request, key,
            RequestCorrelation.Read(HttpContext)), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
