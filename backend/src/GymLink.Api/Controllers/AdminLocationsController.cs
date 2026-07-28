using GymLink.Application.Administration;
using GymLink.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GymLink.Api.Controllers;

[ApiController]
[Authorize(Policy = PolicyNames.CentralAdminOnly)]
[EnableRateLimiting("LocationSearch")]
[Route("api/admin/locations")]
public sealed class AdminLocationsController(ILocationSearchService service) : ControllerBase
{
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] LocationSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.SearchAsync(request, cancellationToken));
}
