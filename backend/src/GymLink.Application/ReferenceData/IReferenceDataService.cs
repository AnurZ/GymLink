using GymLink.Application.Common;

namespace GymLink.Application.ReferenceData;

public interface IReferenceDataService
{
    Task<PagedResult<CountryDto>> SearchCountriesAsync(ReferenceSearchRequest request, CancellationToken cancellationToken);
    Task<CountryDto> CreateCountryAsync(CreateCountryRequest request, CancellationToken cancellationToken);
    Task<CountryDto> UpdateCountryAsync(Guid id, UpdateCountryRequest request, CancellationToken cancellationToken);
    Task DeleteCountryAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<CityDto>> SearchCitiesAsync(CitySearchRequest request, CancellationToken cancellationToken);
    Task<CityDto> CreateCityAsync(CreateCityRequest request, CancellationToken cancellationToken);
    Task<CityDto> UpdateCityAsync(Guid id, UpdateCityRequest request, CancellationToken cancellationToken);
    Task DeleteCityAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<EquipmentDto>> SearchEquipmentAsync(ReferenceSearchRequest request, CancellationToken cancellationToken);
    Task<EquipmentDto> CreateEquipmentAsync(CreateEquipmentRequest request, CancellationToken cancellationToken);
    Task<EquipmentDto> UpdateEquipmentAsync(Guid id, UpdateEquipmentRequest request, CancellationToken cancellationToken);
    Task DeleteEquipmentAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<TrainingTypeDto>> SearchTrainingTypesAsync(ReferenceSearchRequest request, CancellationToken cancellationToken);
    Task<TrainingTypeDto> CreateTrainingTypeAsync(CreateTrainingTypeRequest request, CancellationToken cancellationToken);
    Task<TrainingTypeDto> UpdateTrainingTypeAsync(Guid id, UpdateTrainingTypeRequest request, CancellationToken cancellationToken);
    Task DeleteTrainingTypeAsync(Guid id, CancellationToken cancellationToken);

    Task<ReferenceLookupsDto> GetActiveLookupsAsync(CancellationToken cancellationToken);
}
