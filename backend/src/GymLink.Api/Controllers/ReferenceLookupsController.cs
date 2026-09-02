using GymLink.Application.ReferenceData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Route("api/reference-data")]
[Authorize]
public sealed class ReferenceLookupsController(IReferenceDataService service) : ControllerBase
{
    [HttpGet("lookups")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        Ok(await service.GetActiveLookupsAsync(cancellationToken));
}
