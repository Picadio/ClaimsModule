using ClaimsModule.API.Correlation;
using ClaimsModule.Application.Claims.Documents;
using ClaimsModule.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsModule.API.Controllers;

[Authorize]
[ApiController]
[Route("api/claims/{id:guid}/documents")]
[ResponseCache(NoStore = true)]
public sealed class DocumentsController(ISender sender) : ControllerBase
{
    private const long MultipartLimit = DocumentFileRules.MaxBytes + 128 * 1024;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClaimDocumentDto>>> List(Guid id, CancellationToken cancellationToken)
    {
        var items = await sender.Send(new ListClaimDocumentsQuery(id), cancellationToken);
        return items is null ? NotFound() : Ok(items);
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MultipartLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = MultipartLimit)]
    [ProducesResponseType<ClaimDocumentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClaimDocumentDto>> Upload(Guid id, [FromForm] DocumentUploadForm form,
        [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken cancellationToken)
    {
        if (form.File is not { } file || Request.Form.Files.Count != 1)
        {
            throw new ClaimRuleViolation("File", "Upload exactly one file.");
        }

        await using var content = file.OpenReadStream();
        var document = await sender.Send(new UploadClaimDocumentCommand(id, file.FileName, file.ContentType, content,
            form.DocumentType, form.Notes, key, RequestCorrelation.Read(HttpContext)), cancellationToken);
        return document is null ? NotFound() : CreatedAtAction(nameof(List), new { id }, document);
    }

    [HttpGet("{documentId:guid}/download")]
    public async Task<IActionResult> Download(Guid id, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await sender.Send(new DownloadLocalClaimDocumentQuery(id, documentId), cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(document.Content, document.ContentType, document.FileName);
    }
}

public sealed class DocumentUploadForm
{
    public IFormFile? File { get; init; }
    public string DocumentType { get; init; } = "Other";
    public string? Notes { get; init; }
}
