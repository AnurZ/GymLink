using GymLink.Application.Identity;
using GymLink.Application.Authorization;
using GymLink.Application.TrainerImages;
using GymLink.Domain.Trainers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/profile")]
public sealed class ProfileController(
    IProfileService profileService,
    ITrainerImageService trainerImages) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserProfileDto>> Get(CancellationToken cancellationToken) =>
        Ok(await profileService.GetAsync(cancellationToken));

    [HttpPut]
    public async Task<ActionResult<UserProfileDto>> Update(
        UpdateProfileRequest request,
        CancellationToken cancellationToken) =>
        Ok(await profileService.UpdateAsync(request, cancellationToken));

    [HttpPost("trainer-image")]
    [Authorize(Policy = PolicyNames.TenantTrainer)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(TrainerProfile.MaximumImageFileSizeBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = TrainerProfile.MaximumImageFileSizeBytes + 65536)]
    public async Task<ActionResult<TrainerImageDto>> UploadTrainerImage(
        [FromForm] TrainerImageUploadForm form,
        CancellationToken cancellationToken) =>
        Ok(await trainerImages.UploadOwnAsync(
            await form.ToUploadAsync(cancellationToken),
            cancellationToken));

    [HttpDelete("trainer-image")]
    [Authorize(Policy = PolicyNames.TenantTrainer)]
    public async Task<ActionResult<TrainerImageDto>> RemoveTrainerImage(
        TrainerImageMutationRequest request,
        CancellationToken cancellationToken) =>
        Ok(await trainerImages.RemoveOwnAsync(request, cancellationToken));

    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        await profileService.ChangePasswordAsync(request, cancellationToken);
        return NoContent();
    }
}
