using AutoMapper;
using GymLink.Domain.Memberships;
using GymLink.Domain.Trainers;

namespace GymLink.Application.Catalog;

public sealed class CatalogProfile : Profile
{
    public CatalogProfile()
    {
        CreateMap<MembershipPlan, MembershipPlanDto>();
        CreateMap<TrainerServiceOffering, TrainerOfferingDto>()
            .ForCtorParam(nameof(TrainerOfferingDto.TrainingType), options => options.MapFrom(_ => string.Empty));
    }
}
