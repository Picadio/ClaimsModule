using AutoMapper;
using MediatR;

namespace ClaimsModule.Application.Claims;

public sealed record GetClaimQuery(Guid Id) : IRequest<ClaimDetail?>;

internal sealed class GetClaimHandler(IClaimStore store, IMapper mapper) : IRequestHandler<GetClaimQuery, ClaimDetail?>
{
    public async Task<ClaimDetail?> Handle(GetClaimQuery request, CancellationToken cancellationToken)
    {
        var claim = await store.FindAsync(request.Id, cancellationToken);
        return claim is null ? null : mapper.Map<ClaimDetail>(claim);
    }
}
