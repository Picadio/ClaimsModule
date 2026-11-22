using AutoMapper;
using ClaimsModule.Application.Security;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Exceptions;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Claims;

public sealed record UpdateClaimNotesRequest(string? Notes);
public sealed record ClaimNotesResult(Guid ClaimId, string? Notes);
public sealed record LinkClaimPolicyRequest(Guid PolicyId, bool AcknowledgePolicyPeriodWarning = false);
public sealed record ClaimPolicyResult(Guid ClaimId, Guid PolicyId, string? PolicyNumber, string? ClientName,
    IReadOnlyList<ClaimWarningDto> Warnings);

public sealed record UpdateClaimNotesCommand(Guid ClaimId, UpdateClaimNotesRequest Request,
    string? IdempotencyKey, Guid CorrelationId) : IRequest<ClaimNotesResult?>;

public sealed record LinkClaimPolicyCommand(Guid ClaimId, LinkClaimPolicyRequest Request,
    string? IdempotencyKey, Guid CorrelationId) : IRequest<ClaimPolicyResult?>;

internal sealed class UpdateClaimNotesValidator : AbstractValidator<UpdateClaimNotesCommand>
{
    public UpdateClaimNotesValidator()
    {
        RuleFor(command => command.Request.Notes).MaximumLength(4000);
        RuleFor(command => command.IdempotencyKey).OptionalKey();
    }
}

internal sealed class LinkClaimPolicyValidator : AbstractValidator<LinkClaimPolicyCommand>
{
    public LinkClaimPolicyValidator()
    {
        RuleFor(command => command.Request.PolicyId).NotEmpty();
        RuleFor(command => command.IdempotencyKey).OptionalKey();
    }
}

internal sealed class ClaimOverviewHandler(IClaimStore claims, IClaimLifecycleStore lifecycle, IClaimUnitOfWork unitOfWork,
    ICurrentUser user, ClaimMutationReplays replays, IMapper mapper, IAuditLogService audit, TimeProvider clock)
    : IRequestHandler<UpdateClaimNotesCommand, ClaimNotesResult?>, IRequestHandler<LinkClaimPolicyCommand, ClaimPolicyResult?>
{
    public Task<ClaimNotesResult?> Handle(UpdateClaimNotesCommand command, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteClaimWriteAsync<ClaimNotesResult?>(command.ClaimId, async token =>
        {
            var claim = await claims.FindForWriteAsync(command.ClaimId, token);
            if (claim is null)
            {
                return null;
            }

            var actor = user.Identity.UserId;
            var key = ClaimMutationKey.Create(command.IdempotencyKey, new { Action = "UpdateNotes", command.Request });
            if (await replays.ReadAsync<ClaimNotesResult>(claim.Id, actor, key, token) is { } prior)
            {
                return prior;
            }

            var previous = claim.Notes;
            var now = clock.GetUtcNow();
            if (claim.SetNotes(command.Request.Notes, actor, now))
            {
                await unitOfWork.SaveAsync(token);
                audit.Append(new ClaimAuditEvent(claim.OrganizationEntityId, claim.Id, actor, now, command.CorrelationId,
                    "CLAIM_NOTES_UPDATED", "The claim notes were updated.", new { Notes = previous }, new { claim.Notes }));
            }

            var result = new ClaimNotesResult(claim.Id, claim.Notes);
            replays.Record(claim.Id, actor, now, key, result);
            await unitOfWork.SaveAsync(token);
            return result;
        }, cancellationToken);

    public Task<ClaimPolicyResult?> Handle(LinkClaimPolicyCommand command, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteClaimWriteAsync<ClaimPolicyResult?>(command.ClaimId, async token =>
        {
            var claim = await lifecycle.FindForUpdateAsync(command.ClaimId, token);
            if (claim is null)
            {
                return null;
            }

            var actor = user.Identity.UserId;
            var key = ClaimMutationKey.Create(command.IdempotencyKey, new { Action = "LinkPolicy", command.Request });
            if (await replays.ReadAsync<ClaimPolicyResult>(claim.Id, actor, key, token) is { } prior)
            {
                return prior;
            }

            var policy = await claims.FindPolicyAsync(command.Request.PolicyId, token)
                ?? throw new ClaimRuleViolation("PolicyId", "Select a policy from this organization.");
            var previous = claim.PolicyId;
            var now = clock.GetUtcNow();
            if (claim.LinkPolicy(policy, command.Request.AcknowledgePolicyPeriodWarning, actor, now) is { } change)
            {
                await unitOfWork.SaveAsync(token);
                AppendPolicyAudit(claim, change, previous, actor, now, command.CorrelationId);
            }

            var result = new ClaimPolicyResult(claim.Id, policy.Id, claim.PolicyNumber, claim.ClientName,
                mapper.Map<ClaimWarningDto[]>(claim.Warnings.Where(warning => !warning.IsDeleted)));
            replays.Record(claim.Id, actor, now, key, result);
            await unitOfWork.SaveAsync(token);
            return result;
        }, cancellationToken);

    private void AppendPolicyAudit(Claim claim, ClaimPolicyChange change, Guid? previous, Guid actor, DateTimeOffset now,
        Guid correlationId)
    {
        audit.Append(new ClaimAuditEvent(claim.OrganizationEntityId, claim.Id, actor, now, correlationId,
            "POLICY_LINKED", "A policy was linked to the claim.", new { PolicyId = previous },
            new { claim.PolicyId, claim.PolicyNumber }, claim.PolicyId, "Policy"));
        var position = 1;
        if (change.ResolvedWarning is { } resolved)
        {
            AppendWarning("VALIDATION_ISSUE_RESOLVED", resolved);
        }

        if (change.AddedWarning is { } added)
        {
            AppendWarning("VALIDATION_ISSUE_ADDED", added);
            if (change.Acknowledged)
            {
                AppendWarning("VALIDATION_WARNING_ACKNOWLEDGED", added);
            }
        }

        void AppendWarning(string type, ClaimWarning warning) =>
            audit.Append(new ClaimAuditEvent(claim.OrganizationEntityId, claim.Id, actor, now, correlationId, type,
                warning.Message, NewValues: new { warning.Code, warning.Field, Severity = "Warning" },
                RelatedEntityId: warning.Id, RelatedEntityType: nameof(ClaimWarning), Position: position++));
    }
}
