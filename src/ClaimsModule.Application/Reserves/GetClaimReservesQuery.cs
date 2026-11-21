using MediatR;

namespace ClaimsModule.Application.Reserves;

public sealed record GetClaimReservesQuery(Guid ClaimId) : IRequest<ClaimReserveLedger?>;

internal sealed class GetClaimReservesHandler(IReserveStore store) : IRequestHandler<GetClaimReservesQuery, ClaimReserveLedger?>
{
    public async Task<ClaimReserveLedger?> Handle(GetClaimReservesQuery query, CancellationToken cancellationToken)
    {
        var ledger = await store.ReadLedgerAsync(query.ClaimId, cancellationToken);
        if (ledger is null)
        {
            return null;
        }

        var history = ledger.History.ToLookup(entry => entry.ReserveId);
        var components = ledger.Balances.Select(balance => new ReserveComponentLedger(balance.Id, balance.Component,
            balance.CurrentAmount, balance.PendingAmount,
            history[balance.Id].Select(entry => ReserveTransaction.From(entry, balance.Component)).ToArray())).ToArray();
        return new ClaimReserveLedger(query.ClaimId, components.Sum(component => component.CurrentAmount), components)
        {
            ReserveLimitOverrideEnabled = ledger.ReserveLimitOverrideEnabled
        };
    }
}
