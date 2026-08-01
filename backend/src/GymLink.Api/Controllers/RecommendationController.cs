using GymLink.Application.Authorization;
using GymLink.Application.Recommendations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Authorize(Policy = PolicyNames.MemberSelf)]
[Route("api/me")]
public sealed class RecommendationController(IRecommendationService service) : ControllerBase
{
    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken cancellationToken) =>
        Ok(await service.GetPreferencesAsync(cancellationToken));

    [HttpPut("preferences")]
    public async Task<IActionResult> ReplacePreferences(
        ReplacePreferencesRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.ReplacePreferencesAsync(request, cancellationToken));

    [HttpGet("recommendations")]
    public async Task<IActionResult> GetRecommendations(
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default) =>
        Ok(await service.GetAsync(limit, cancellationToken));

    [HttpPost("recommendations/refresh")]
    public async Task<IActionResult> RefreshRecommendations(
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default) =>
        Ok(await service.RefreshAsync(limit, cancellationToken));
}
