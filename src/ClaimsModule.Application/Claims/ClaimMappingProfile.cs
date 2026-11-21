using AutoMapper;
using ClaimsModule.Domain.Claims;

namespace ClaimsModule.Application.Claims;

public sealed class ClaimMappingProfile : Profile
{
    public ClaimMappingProfile()
    {
        CreateMap<LossEvent, LossDetails>();
        CreateMap<ClaimParty, ClaimPartyDto>();
        CreateMap<ClaimRiskObject, ClaimRiskDto>();
        CreateMap<ClaimWarning, ClaimWarningDto>();
        CreateMap<Claim, CreatedClaim>();
        CreateMap<Claim, ClaimDetail>()
            .ForCtorParam(nameof(ClaimDetail.ReportedAt), member => member.MapFrom(claim => claim.CreatedAt))
            .ForMember(detail => detail.RecentAuditEntries, member => member.Ignore());
    }
}
