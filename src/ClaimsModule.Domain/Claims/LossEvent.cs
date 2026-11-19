using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Claims;

public sealed class LossEvent : OrganizationRecord
{
    public Guid ClaimId { get; private set; }
    public DateTimeOffset LossDate { get; private set; }
    public string CauseOfLossCode { get; private set; } = null!;
    public string LossDescription { get; private set; } = null!;
    public string? LossLocation { get; private set; }
    public decimal? EstimatedLossAmount { get; private set; }
    public string? PoliceReportNumber { get; private set; }
    
    private LossEvent()
    {
    }

    internal LossEvent(Guid organizationId, Guid actorId, DateTimeOffset reportedAt, LossDetails details)
        : base(organizationId, actorId, reportedAt)
    {
        LossDate = details.LossDate.ToUniversalTime();
        CauseOfLossCode = details.CauseOfLossCode.Trim().ToUpperInvariant();
        LossDescription = details.LossDescription.Trim();
        LossLocation = details.LossLocation?.Trim();
        EstimatedLossAmount = details.EstimatedLossAmount;
        PoliceReportNumber = details.PoliceReportNumber?.Trim();
    }
}
