using GymLink.Application.Administration;
using GymLink.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Authorize(Policy = PolicyNames.CentralAdminOnly)]
[Route("api/admin/gyms")]
public sealed class AdminGymsController(IGymAdministrationService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] AdminGymSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.SearchAsync(request, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateAdminGymRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return Created($"/api/admin/gyms/{result.Id}", result);
    }
}
