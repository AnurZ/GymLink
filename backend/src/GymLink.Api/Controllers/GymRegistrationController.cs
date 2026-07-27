using GymLink.Application.Authorization;
using GymLink.Application.Registration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/gym-registration-requests")]
public sealed class GymRegistrationController(
    IGymRegistrationService registrationService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<GymRegistrationDto>> Submit(
        SubmitGymRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await registrationService.SubmitAsync(request, cancellationToken);
        return Created($"/api/gym-registration-requests/{result.Id}", result);
    }

    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<GymRegistrationDto>>> Mine(
        CancellationToken cancellationToken) =>
        Ok(await registrationService.ListMineAsync(cancellationToken));
}

[ApiController]
[Authorize(Policy = PolicyNames.CentralAdminOnly)]
[Route("api/admin/gym-registration-requests")]
public sealed class AdminGymRegistrationController(
    IGymRegistrationService registrationService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] RegistrationSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await registrationService.SearchAsync(request, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await registrationService.GetAsync(id, cancellationToken));

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(
        Guid id,
        RegistrationDecisionRequest request,
        CancellationToken cancellationToken) =>
        Ok(await registrationService.ApproveAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(
        Guid id,
        RegistrationDecisionRequest request,
        CancellationToken cancellationToken) =>
        Ok(await registrationService.RejectAsync(id, request, cancellationToken));
}
