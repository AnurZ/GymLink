using GymLink.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/profile")]
public sealed class ProfileController(IProfileService profileService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserProfileDto>> Get(CancellationToken cancellationToken) =>
        Ok(await profileService.GetAsync(cancellationToken));

    [HttpPut]
    public async Task<ActionResult<UserProfileDto>> Update(
        UpdateProfileRequest request,
        CancellationToken cancellationToken) =>
        Ok(await profileService.UpdateAsync(request, cancellationToken));

    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        await profileService.ChangePasswordAsync(request, cancellationToken);
        return NoContent();
    }
}
