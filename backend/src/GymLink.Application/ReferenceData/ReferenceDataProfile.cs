using AutoMapper;
using GymLink.Domain.ReferenceData;

namespace GymLink.Application.ReferenceData;

public sealed class ReferenceDataProfile : Profile
{
    public ReferenceDataProfile()
    {
        CreateMap<Country, CountryDto>();
        CreateMap<Equipment, EquipmentDto>();
        CreateMap<TrainingType, TrainingTypeDto>();
    }
}
