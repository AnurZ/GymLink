using GymLink.Application.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[AllowAnonymous]
public sealed class PublicCatalogController(
    IGymCatalogService gyms,
    ITrainerCatalogService trainers,
    IMembershipPlanService plans,
    ITrainerOfferingService offerings) : ControllerBase
{
    [HttpGet("api/gyms")]
    public async Task<IActionResult> SearchGyms(
        [FromQuery] GymSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await gyms.SearchPublicAsync(request, cancellationToken));

    [HttpGet("api/gyms/{id:guid}")]
    public async Task<IActionResult> GetGym(Guid id, CancellationToken cancellationToken) =>
        Ok(await gyms.GetPublicDetailsAsync(id, cancellationToken));

    [HttpGet("api/gyms/{gymId:guid}/trainers")]
    public async Task<IActionResult> GetGymTrainers(Guid gymId, CancellationToken cancellationToken) =>
        Ok(await trainers.GetPublicByGymAsync(gymId, cancellationToken));

    [HttpGet("api/gyms/{gymId:guid}/membership-plans")]
    public async Task<IActionResult> GetGymPlans(Guid gymId, CancellationToken cancellationToken) =>
        Ok(await plans.GetPublicByGymAsync(gymId, cancellationToken));

    [HttpGet("api/trainers/{trainerId:guid}/offerings")]
    public async Task<IActionResult> GetTrainerOfferings(
        Guid trainerId,
        CancellationToken cancellationToken) =>
        Ok(await offerings.GetPublicByTrainerAsync(trainerId, cancellationToken));
}
