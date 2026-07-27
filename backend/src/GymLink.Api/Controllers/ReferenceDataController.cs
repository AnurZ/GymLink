using GymLink.Application.Authorization;
using GymLink.Application.ReferenceData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Route("api/admin/reference-data")]
[Authorize(Policy = PolicyNames.CentralAdminOnly)]
public sealed class ReferenceDataController(IReferenceDataService service) : ControllerBase
{
    [HttpGet("countries")]
    public async Task<IActionResult> SearchCountries(
        [FromQuery] ReferenceSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.SearchCountriesAsync(request, cancellationToken));

    [HttpPost("countries")]
    public async Task<IActionResult> CreateCountry(
        CreateCountryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateCountryAsync(request, cancellationToken);
        return Created($"/api/admin/reference-data/countries/{result.Id}", result);
    }

    [HttpPut("countries/{id:guid}")]
    public async Task<IActionResult> UpdateCountry(
        Guid id,
        UpdateCountryRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.UpdateCountryAsync(id, request, cancellationToken));

    [HttpDelete("countries/{id:guid}")]
    public async Task<IActionResult> DeleteCountry(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteCountryAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("cities")]
    public async Task<IActionResult> SearchCities(
        [FromQuery] CitySearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.SearchCitiesAsync(request, cancellationToken));

    [HttpPost("cities")]
    public async Task<IActionResult> CreateCity(CreateCityRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateCityAsync(request, cancellationToken);
        return Created($"/api/admin/reference-data/cities/{result.Id}", result);
    }

    [HttpPut("cities/{id:guid}")]
    public async Task<IActionResult> UpdateCity(
        Guid id,
        UpdateCityRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.UpdateCityAsync(id, request, cancellationToken));

    [HttpDelete("cities/{id:guid}")]
    public async Task<IActionResult> DeleteCity(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteCityAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("equipment")]
    public async Task<IActionResult> SearchEquipment(
        [FromQuery] ReferenceSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.SearchEquipmentAsync(request, cancellationToken));

    [HttpPost("equipment")]
    public async Task<IActionResult> CreateEquipment(
        CreateEquipmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateEquipmentAsync(request, cancellationToken);
        return Created($"/api/admin/reference-data/equipment/{result.Id}", result);
    }

    [HttpPut("equipment/{id:guid}")]
    public async Task<IActionResult> UpdateEquipment(
        Guid id,
        UpdateEquipmentRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.UpdateEquipmentAsync(id, request, cancellationToken));

    [HttpDelete("equipment/{id:guid}")]
    public async Task<IActionResult> DeleteEquipment(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteEquipmentAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("training-types")]
    public async Task<IActionResult> SearchTrainingTypes(
        [FromQuery] ReferenceSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.SearchTrainingTypesAsync(request, cancellationToken));

    [HttpPost("training-types")]
    public async Task<IActionResult> CreateTrainingType(
        CreateTrainingTypeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateTrainingTypeAsync(request, cancellationToken);
        return Created($"/api/admin/reference-data/training-types/{result.Id}", result);
    }

    [HttpPut("training-types/{id:guid}")]
    public async Task<IActionResult> UpdateTrainingType(
        Guid id,
        UpdateTrainingTypeRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.UpdateTrainingTypeAsync(id, request, cancellationToken));

    [HttpDelete("training-types/{id:guid}")]
    public async Task<IActionResult> DeleteTrainingType(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteTrainingTypeAsync(id, cancellationToken);
        return NoContent();
    }
}
