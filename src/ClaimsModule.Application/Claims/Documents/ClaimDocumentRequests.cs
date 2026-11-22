using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Claims;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClaimsModule.Application.Claims.Documents;

public sealed record UploadClaimDocumentCommand(Guid ClaimId, string FileName, string ContentType, Stream Content,
    string DocumentType, string? Notes, string? IdempotencyKey, Guid CorrelationId) : IRequest<ClaimDocumentDto?>;

public sealed record ListClaimDocumentsQuery(Guid ClaimId) : IRequest<IReadOnlyList<ClaimDocumentDto>?>;

public sealed record DownloadLocalClaimDocumentQuery(Guid ClaimId, Guid DocumentId) : IRequest<LocalClaimDocument?>;

internal sealed class UploadClaimDocumentValidator : AbstractValidator<UploadClaimDocumentCommand>
{
    public UploadClaimDocumentValidator()
    {
        RuleFor(command => command.FileName).NotEmpty().MaximumLength(512);
        RuleFor(command => command.DocumentType).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Notes).MaximumLength(2000);
        RuleFor(command => command.IdempotencyKey).OptionalKey();
    }
}

internal sealed class ClaimDocumentQueries(IClaimDocumentStore documents, IStorageService storage, DocumentLinks links)
    : IRequestHandler<ListClaimDocumentsQuery, IReadOnlyList<ClaimDocumentDto>?>,
      IRequestHandler<DownloadLocalClaimDocumentQuery, LocalClaimDocument?>
{
    public async Task<IReadOnlyList<ClaimDocumentDto>?> Handle(ListClaimDocumentsQuery query,
        CancellationToken cancellationToken)
    {
        var rows = await documents.ListAsync(query.ClaimId, cancellationToken);
        return rows?.Select(links.Create).ToArray();
    }

    public async Task<LocalClaimDocument?> Handle(DownloadLocalClaimDocumentQuery query, CancellationToken cancellationToken)
    {
        var row = await documents.FindAsync(query.ClaimId, query.DocumentId, cancellationToken);
        if (row?.StorageProvider != DocumentStorageProvider.LocalFileSystem ||
            storage.Provider != row.StorageProvider || storage.OpenLocalRead(row.StorageKey) is not { } content)
        {
            return null;
        }

        return new LocalClaimDocument(row.FileName, row.ContentType, content);
    }
}

internal sealed class UploadClaimDocumentHandler(IClaimStore claims, IClaimDocumentStore documents, IStorageService storage,
    IClaimUnitOfWork unitOfWork, ICurrentUser user, ClaimMutationReplays replays, IAuditLogService audit,
    DocumentLinks links, TimeProvider clock, ILogger<UploadClaimDocumentHandler> logger)
    : IRequestHandler<UploadClaimDocumentCommand, ClaimDocumentDto?>
{
    public async Task<ClaimDocumentDto?> Handle(UploadClaimDocumentCommand command, CancellationToken cancellationToken)
    {
        var file = await DocumentFileRules.ValidateAsync(command.FileName, command.ContentType, command.Content, cancellationToken);
        var type = command.DocumentType.Trim();
        var notes = command.Notes?.Trim();
        var actor = user.Identity.UserId;
        var key = ClaimMutationKey.Create(command.IdempotencyKey,
            new { Action = "UploadDocument", file.FileName, file.ContentType, file.Sha256, DocumentType = type, Notes = notes });
        var document = await unitOfWork.ExecuteClaimWriteAsync<ClaimDocument?>(command.ClaimId, async token =>
        {
            var claim = await claims.FindForWriteAsync(command.ClaimId, token);
            if (claim is null)
            {
                return null;
            }

            if (await replays.ReadAsync<DocumentReceipt>(claim.Id, actor, key, token) is { } prior)
            {
                return await documents.FindAsync(claim.Id, prior.Id, token);
            }

            var storageKey = $"{claim.OrganizationEntityId:D}/{claim.Id:D}/{Guid.NewGuid():N}-{file.FileName}";
            await storage.UploadAsync(storageKey, file, command.Content, token);
            try
            {
                var now = clock.GetUtcNow();
                var added = new ClaimDocument(claim.OrganizationEntityId, claim.Id, actor, now, file.FileName,
                    storageKey, storage.Provider, file.ContentType, file.SizeBytes, type, notes);
                claim.RecordActivity(actor, now);
                await unitOfWork.SaveAsync(token);
                documents.Add(added);
                await unitOfWork.SaveAsync(token);
                audit.Append(new ClaimAuditEvent(claim.OrganizationEntityId, claim.Id, actor, now, command.CorrelationId,
                    "DOCUMENT_UPLOADED", "A document was uploaded to the claim.",
                    NewValues: new { added.FileName, added.DocumentType, added.SizeBytes },
                    RelatedEntityId: added.Id, RelatedEntityType: nameof(ClaimDocument)));
                replays.Record(claim.Id, actor, now, key, new DocumentReceipt(added.Id));
                await unitOfWork.SaveAsync(token);
                return added;
            }
            catch
            {
                await DeleteUncommittedFileAsync(storageKey);
                throw;
            }
        }, cancellationToken);

        return document is null ? null : links.Create(document);
    }

    private async Task DeleteUncommittedFileAsync(string key)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await storage.DeleteAsync(key, timeout.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not clean up an uncommitted claim document.");
        }
    }

    private sealed record DocumentReceipt(Guid Id);
}
