using ClaimsModule.Application.Reference;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsModule.API.Controllers;

[Authorize]
[ApiController]
[Route("api/policies")]
public sealed class PoliciesController(ISender sender) : ControllerBase
{
    [HttpGet("search")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<IReadOnlyList<PolicySearchResult>> Search([FromQuery] string? q, CancellationToken cancellationToken) =>
        sender.Send(new SearchPoliciesQuery(q), cancellationToken);

    [HttpGet("{id:guid}/coverage")]
    [ProducesResponseType<PolicyCoverage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PolicyCoverage>> GetCoverage(Guid id, CancellationToken cancellationToken)
    {
        var coverage = await sender.Send(new GetPolicyCoverageQuery(id), cancellationToken);
        return coverage is null ? NotFound() : Ok(coverage);
    }
}
