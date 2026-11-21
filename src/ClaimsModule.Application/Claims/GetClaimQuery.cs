using AutoMapper;
using MediatR;

namespace ClaimsModule.Application.Claims;

public sealed record GetClaimQuery(Guid Id) : IRequest<ClaimDetail?>;

internal sealed class GetClaimHandler(IClaimStore store, IClaimAuditReader audit, IMapper mapper)
    : IRequestHandler<GetClaimQuery, ClaimDetail?>
{
    public async Task<ClaimDetail?> Handle(GetClaimQuery request, CancellationToken cancellationToken)
    {
        var claim = await store.FindAsync(request.Id, cancellationToken);
        if (claim is null)
        {
            return null;
        }

        var recent = await audit.ReadPageAsync(claim.Id, page: 1, pageSize: 10, cancellationToken);
        return recent is null ? null : mapper.Map<ClaimDetail>(claim) with { RecentAuditEntries = recent.Items };
    }
}
