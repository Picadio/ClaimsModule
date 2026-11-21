using ClaimsModule.Application.Reference;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsModule.API.Controllers;

[Authorize]
[ApiController]
[Route("api/reference")]
public sealed class ReferenceController(ISender sender) : ControllerBase
{
    [HttpGet("cause-of-loss-codes")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<IReadOnlyList<CauseOfLossCode>> ListCauseOfLossCodes([FromQuery] string? perilCategory,
        CancellationToken cancellationToken) => sender.Send(new ListCauseOfLossCodesQuery(perilCategory), cancellationToken);

    [HttpGet("claim-statuses")]
    public Task<IReadOnlyList<ClaimStatusReference>> ListClaimStatuses(CancellationToken cancellationToken) =>
        sender.Send(new ListClaimStatusesQuery(), cancellationToken);
}
