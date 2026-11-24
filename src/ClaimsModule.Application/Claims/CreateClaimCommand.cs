using AutoMapper;
using ClaimsModule.Application.Exceptions;
using ClaimsModule.Application.Reserves;
using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Claims;
using MediatR;

namespace ClaimsModule.Application.Claims;

public sealed record CreateClaimCommand(CreateClaimRequest Request, string? IdempotencyKey, Guid CorrelationId)
    : IRequest<CreatedClaim>;

internal sealed class CreateClaimHandler(IClaimStore store, IClaimUnitOfWork unitOfWork, ICurrentUser currentUser,
    TimeProvider clock, IPublisher publisher, IMapper mapper, ReserveWriter reserveWriter, ClaimIntake intake)
    : IRequestHandler<CreateClaimCommand, CreatedClaim>
{
    public async Task<CreatedClaim> Handle(CreateClaimCommand command, CancellationToken cancellationToken)
    {
        var user = currentUser.Identity;
        var createdClaim = await unitOfWork.ExecuteIntakeAsync(async token =>
        {
            var request = command.Request;
            var keyHash = IdempotencyFingerprint.HashKey(command.IdempotencyKey);
            var requestHash = keyHash is null ? null : IdempotencyFingerprint.HashRequest(request);

            if (keyHash is not null && await store.FindSubmissionAsync(user.UserId, keyHash, token) is { } prior)
            {
                return prior.RequestHash == requestHash ? prior.Response : throw new IdempotencyConflictException();
            }

            var reportedAt = clock.GetUtcNow();
            var sequence = await store.NextSequenceAsync(reportedAt.UtcDateTime.Year, token);
            var claim = await intake.PrepareAsync(request, user, reportedAt, sequence, token);

            store.Add(claim);
            await unitOfWork.SaveAsync(token);
            await publisher.Publish(new ClaimCreatedNotification(claim, claim.CreatedEvent!, command.CorrelationId), token);

            var response = mapper.Map<CreatedClaim>(claim);
            if (request.InitialReserve is { } initial)
            {
                var reserve = await reserveWriter.SubmitAsync(claim, null, initial.Component!.Value,
                    ReserveWriter.InitialChange(initial.Amount),
                    user.UserId, reportedAt, command.CorrelationId, 1 + claim.Warnings.Count + claim.Parties.Count, token);
                response = response with { InitialReserve = reserve };
            }

            if (keyHash is not null)
            {
                store.AddSubmission(new StoredClaimSubmission(user.UserId, keyHash, requestHash!, response));
            }

            await unitOfWork.SaveAsync(token);
            return response;
        }, cancellationToken);

        await ReservePostingReady.RequestAsync(publisher, user.OrganizationEntityId, createdClaim.Id,
            createdClaim.InitialReserve, cancellationToken);
        return createdClaim;
    }
}

internal sealed record ClaimCreatedNotification(Claim Claim, ClaimCreated Event, Guid CorrelationId) : INotification;

internal sealed class RecordClaimCreatedAudit(IAuditLogService audit) : INotificationHandler<ClaimCreatedNotification>
{
    public Task Handle(ClaimCreatedNotification notification, CancellationToken cancellationToken)
    {
        var (claim, created, correlationId) = notification;
        audit.Append(new ClaimAuditEvent(claim.OrganizationEntityId, claim.Id, claim.CreatedByUserId, created.OccurredAt,
            correlationId, "CLAIM_CREATED", "A claim was registered.", NewValues: new
            {
                claim.ClaimNumber,
                Status = claim.Status.ToString(),
                created.OccurredAt
            }));

        var position = 1;
        foreach (var warning in claim.Warnings)
        {
            audit.Append(new ClaimAuditEvent(claim.OrganizationEntityId, claim.Id, claim.CreatedByUserId, created.OccurredAt,
                correlationId, "VALIDATION_ISSUE_ADDED", warning.Message,
                NewValues: new { warning.Code, warning.Field, Severity = "Warning" }, RelatedEntityId: warning.Id,
                RelatedEntityType: nameof(ClaimWarning), Position: position++));
        }

        foreach (var party in claim.Parties)
        {
            audit.Append(ClaimAuditEvent.PartyAdded(claim, party, claim.CreatedByUserId, created.OccurredAt,
                correlationId, position++));
        }

        return Task.CompletedTask;
    }
}
