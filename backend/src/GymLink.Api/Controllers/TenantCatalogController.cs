using GymLink.Application.Authorization;
using GymLink.Application.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Route("api/tenant")]
[Authorize(Policy = PolicyNames.TenantGymAdmin)]
public sealed class TenantCatalogController(
    IGymCatalogService gyms,
    ITrainerCatalogService trainers,
    IMembershipPlanService plans,
    ITrainerOfferingService offerings) : ControllerBase
{
    [HttpGet("gym")]
    public async Task<IActionResult> GetGym(CancellationToken cancellationToken) =>
        Ok(await gyms.GetCurrentTenantGymAsync(cancellationToken));

    [HttpPut("gym")]
    public async Task<IActionResult> UpdateGym(
        UpdateGymRequest request,
        CancellationToken cancellationToken) =>
        Ok(await gyms.UpdateCurrentTenantGymAsync(request, cancellationToken));

    [HttpGet("trainers")]
    public async Task<IActionResult> SearchTrainers(
        [FromQuery] TrainerSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await trainers.SearchAsync(request, cancellationToken));

    [HttpPost("trainers")]
    public async Task<IActionResult> CreateTrainer(
        CreateTrainerRequest request,
        CancellationToken cancellationToken)
    {
        var result = await trainers.CreateAsync(request, cancellationToken);
        return Created($"/api/tenant/trainers/{result.Id}", result);
    }

    [HttpPut("trainers/{id:guid}")]
    public async Task<IActionResult> UpdateTrainer(
        Guid id,
        UpdateTrainerRequest request,
        CancellationToken cancellationToken) =>
        Ok(await trainers.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("trainers/{id:guid}")]
    public async Task<IActionResult> DeactivateTrainer(Guid id, CancellationToken cancellationToken)
    {
        await trainers.DeactivateAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("membership-plans")]
    public async Task<IActionResult> SearchPlans(
        [FromQuery] CatalogSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await plans.SearchAsync(request, cancellationToken));

    [HttpPost("membership-plans")]
    public async Task<IActionResult> CreatePlan(
        CreateMembershipPlanRequest request,
        CancellationToken cancellationToken)
    {
        var result = await plans.CreateAsync(request, cancellationToken);
        return Created($"/api/tenant/membership-plans/{result.Id}", result);
    }

    [HttpPut("membership-plans/{id:guid}")]
    public async Task<IActionResult> UpdatePlan(
        Guid id,
        UpdateMembershipPlanRequest request,
        CancellationToken cancellationToken) =>
        Ok(await plans.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("membership-plans/{id:guid}")]
    public async Task<IActionResult> DeactivatePlan(Guid id, CancellationToken cancellationToken)
    {
        await plans.DeactivateAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("trainer-offerings")]
    public async Task<IActionResult> SearchOfferings(
        [FromQuery] CatalogSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await offerings.SearchAsync(request, cancellationToken));

    [HttpPost("trainer-offerings")]
    public async Task<IActionResult> CreateOffering(
        CreateTrainerOfferingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await offerings.CreateAsync(request, cancellationToken);
        return Created($"/api/tenant/trainer-offerings/{result.Id}", result);
    }

    [HttpPut("trainer-offerings/{id:guid}")]
    public async Task<IActionResult> UpdateOffering(
        Guid id,
        UpdateTrainerOfferingRequest request,
        CancellationToken cancellationToken) =>
        Ok(await offerings.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("trainer-offerings/{id:guid}")]
    public async Task<IActionResult> DeactivateOffering(Guid id, CancellationToken cancellationToken)
    {
        await offerings.DeactivateAsync(id, cancellationToken);
        return NoContent();
    }
}
