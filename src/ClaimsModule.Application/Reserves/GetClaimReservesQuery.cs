using ClaimsModule.Application.Security;
using MediatR;

namespace ClaimsModule.Application.Reserves;

public sealed record GetClaimReservesQuery(Guid ClaimId) : IRequest<ClaimReserveLedger?>;

internal sealed class GetClaimReservesHandler(IReserveStore store, IUserStore users, ICurrentUser actor)
    : IRequestHandler<GetClaimReservesQuery, ClaimReserveLedger?>
{
    public async Task<ClaimReserveLedger?> Handle(GetClaimReservesQuery query, CancellationToken cancellationToken)
    {
        var ledger = await store.ReadLedgerAsync(query.ClaimId, cancellationToken);
        if (ledger is null)
        {
            return null;
        }

        var history = ledger.History.ToLookup(entry => entry.ReserveId);
        var approverIds = ledger.History.Select(entry => entry.ApprovedByUserId).OfType<Guid>().Distinct().ToArray();
        var approvers = approverIds.Length == 0 ? new Dictionary<Guid, string>() :
            await users.FindDisplayNamesAsync(actor.Identity.OrganizationEntityId, approverIds, cancellationToken);
        var components = ledger.Balances.Select(balance => new ReserveComponentLedger(balance.Id, balance.Component,
            balance.CurrentAmount, balance.PendingAmount, balance.HasApprovedTransaction, balance.ApprovalStatus,
            history[balance.Id].Select(entry => ReserveTransaction.From(entry, balance.Component) with
            {
                ApprovedByDisplayName = entry.ApprovedByUserId is Guid approver ? approvers.GetValueOrDefault(approver) : null
            }).ToArray())).ToArray();
        return new ClaimReserveLedger(query.ClaimId, components.Sum(component => component.CurrentAmount), components)
        {
            ReserveLimitOverrideEnabled = ledger.ReserveLimitOverrideEnabled
        };
    }
}
