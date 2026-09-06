using GymLink.Application.ReferenceData;
using GymLink.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Route("api/reference-data")]
[Authorize]
public sealed class ReferenceLookupsController(IReferenceDataService service) : ControllerBase
{
    [HttpGet("countries")]
    public async Task<IActionResult> GetCountries(
        [FromQuery] PagedRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.GetActiveCountriesAsync(request, cancellationToken));

    [HttpGet("cities")]
    public async Task<IActionResult> GetCities(
        [FromQuery] PagedRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.GetActiveCitiesAsync(request, cancellationToken));

    [HttpGet("equipment")]
    public async Task<IActionResult> GetEquipment(
        [FromQuery] PagedRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.GetActiveEquipmentAsync(request, cancellationToken));

    [HttpGet("training-types")]
    public async Task<IActionResult> GetTrainingTypes(
        [FromQuery] PagedRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.GetActiveTrainingTypesAsync(request, cancellationToken));
}
