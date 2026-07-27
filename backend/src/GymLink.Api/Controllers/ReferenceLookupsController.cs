using GymLink.Application.ReferenceData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Route("api/reference-data")]
[AllowAnonymous]
public sealed class ReferenceLookupsController(IReferenceDataService service) : ControllerBase
{
    [HttpGet("lookups")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        Ok(await service.GetActiveLookupsAsync(cancellationToken));
}
