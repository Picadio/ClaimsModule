using ClaimsModule.API.Correlation;
using ClaimsModule.API.Security;
using ClaimsModule.Application.Reserves;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsModule.API.Controllers;

[Authorize]
[ApiController]
[Route("api/claims/{id:guid}/reserves")]
public sealed class ReservesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ClaimReserveLedger>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClaimReserveLedger>> List(Guid id, CancellationToken cancellationToken)
    {
        var ledger = await sender.Send(new GetClaimReservesQuery(id), cancellationToken);
        return ledger is null ? NotFound() : Ok(ledger);
    }

    [HttpPost]
    [ProducesResponseType<ReserveTransaction>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReserveTransaction>> Submit(Guid id, SubmitReserveRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        var transaction = await sender.Send(new SubmitReserveCommand(id, null, request, idempotencyKey,
            RequestCorrelation.Read(HttpContext)), cancellationToken);
        return transaction is null ? NotFound() : CreatedAtAction(nameof(List), new { id }, transaction);
    }

    [HttpPut("{reserveId:guid}")]
    [ProducesResponseType<ReserveTransaction>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReserveTransaction>> Adjust(Guid id, Guid reserveId, AdjustReserveRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        var change = new SubmitReserveRequest(null, request.Amount, request.ChangeReason, request.TransactionType,
            request.ReplacesRejectedTransactionId);
        var transaction = await sender.Send(new SubmitReserveCommand(id, reserveId, change, idempotencyKey,
            RequestCorrelation.Read(HttpContext)), cancellationToken);
        return transaction is null ? NotFound() : Ok(transaction);
    }

    [Authorize(Policy = ClaimsAuthorizationPolicies.SupervisorOrManager)]
    [HttpPost("{txnId:guid}/approve")]
    [ProducesResponseType<ReserveTransaction>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ReserveTransaction>> Approve(Guid id, Guid txnId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken) =>
        Resolve(id, txnId, ReserveDecision.Approve, null, idempotencyKey, cancellationToken);

    [Authorize(Policy = ClaimsAuthorizationPolicies.SupervisorOrManager)]
    [HttpPost("{txnId:guid}/reject")]
    [ProducesResponseType<ReserveTransaction>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ReserveTransaction>> Reject(Guid id, Guid txnId, RejectReserveRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken) =>
        Resolve(id, txnId, ReserveDecision.Reject, request.RejectionReason, idempotencyKey, cancellationToken);

    [HttpPost("{txnId:guid}/retract")]
    [ProducesResponseType<ReserveTransaction>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ReserveTransaction>> Retract(Guid id, Guid txnId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken) =>
        Resolve(id, txnId, ReserveDecision.Retract, null, idempotencyKey, cancellationToken);

    [Authorize(Policy = ClaimsAuthorizationPolicies.ManagerOnly)]
    [HttpPut("manager-override")]
    [ProducesResponseType<ReserveOverrideResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReserveOverrideResult>> EnableOverride(Guid id, EnableReserveOverrideRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new EnableReserveOverrideCommand(id, request, idempotencyKey,
            RequestCorrelation.Read(HttpContext)), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{txnId:guid}/retry-posting")]
    [ProducesResponseType<ReserveTransaction>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReserveTransaction>> RetryPosting(Guid id, Guid txnId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RetryReservePostingCommand(id, txnId, idempotencyKey,
            RequestCorrelation.Read(HttpContext)), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    private async Task<ActionResult<ReserveTransaction>> Resolve(Guid id, Guid txnId, ReserveDecision decision,
        string? reason, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var transaction = await sender.Send(new ResolveReserveCommand(id, txnId, decision, reason, idempotencyKey,
            RequestCorrelation.Read(HttpContext)), cancellationToken);
        return transaction is null ? NotFound() : Ok(transaction);
    }
}
