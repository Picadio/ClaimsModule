using ClaimsModule.API.Correlation;
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
        var change = new SubmitReserveRequest(null, request.Amount, request.ChangeReason, request.TransactionType);
        var transaction = await sender.Send(new SubmitReserveCommand(id, reserveId, change, idempotencyKey,
            RequestCorrelation.Read(HttpContext)), cancellationToken);
        return transaction is null ? NotFound() : Ok(transaction);
    }
}
