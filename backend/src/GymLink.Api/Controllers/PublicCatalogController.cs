using GymLink.Application.Catalog;
using GymLink.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Authorize]
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
    public async Task<IActionResult> GetGymTrainers(
        Guid gymId,
        [FromQuery] PagedRequest request,
        CancellationToken cancellationToken) =>
        Ok(await trainers.GetPublicByGymAsync(gymId, request, cancellationToken));

    [HttpGet("api/gyms/{gymId:guid}/membership-plans")]
    public async Task<IActionResult> GetGymPlans(
        Guid gymId,
        [FromQuery] PagedRequest request,
        CancellationToken cancellationToken) =>
        Ok(await plans.GetPublicByGymAsync(gymId, request, cancellationToken));

    [HttpGet("api/trainers/{trainerId:guid}/offerings")]
    public async Task<IActionResult> GetTrainerOfferings(
        Guid trainerId,
        [FromQuery] PagedRequest request,
        CancellationToken cancellationToken) =>
        Ok(await offerings.GetPublicByTrainerAsync(trainerId, request, cancellationToken));
}
