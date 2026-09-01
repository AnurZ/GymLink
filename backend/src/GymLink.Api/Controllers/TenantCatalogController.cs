using GymLink.Application.Authorization;
using GymLink.Application.Catalog;
using GymLink.Application.TrainerImages;
using GymLink.Application.GymImages;
using GymLink.Domain.Catalog;
using GymLink.Domain.Trainers;
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
    ITrainerImageService trainerImages,
    IGymImageService gymImages) : ControllerBase
{
    private const long MaximumGalleryRequestBytes =
        GymImage.MaximumGalleryImages * GymImage.MaximumFileSizeBytes + 262144;
    [HttpGet("gym")]
    public async Task<IActionResult> GetGym(CancellationToken cancellationToken) =>
        Ok(await gyms.GetCurrentTenantGymAsync(cancellationToken));

    [HttpPut("gym")]
    public async Task<IActionResult> UpdateGym(
        UpdateGymRequest request,
        CancellationToken cancellationToken) =>
        Ok(await gyms.UpdateCurrentTenantGymAsync(request, cancellationToken));

    [HttpPost("gym/images")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(GymImage.MaximumFileSizeBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = GymImage.MaximumFileSizeBytes + 65536)]
    public async Task<ActionResult<GymImageGalleryDto>> AddGymImage(
        [FromForm] GymImageUploadForm form,
        CancellationToken cancellationToken) =>
        Ok(await gymImages.AddAsync(
            await form.ToUploadAsync(cancellationToken),
            cancellationToken));

    [HttpPut("gym/images")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumGalleryRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumGalleryRequestBytes)]
    public async Task<ActionResult<GymImageGalleryDto>> SaveGymImages(
        [FromForm] GymImageGallerySaveForm form,
        CancellationToken cancellationToken)
    {
        var request = await form.ToRequestAsync(cancellationToken);
        return Ok(await gymImages.SaveGalleryAsync(
            request.Manifest,
            request.Uploads,
            cancellationToken));
    }

    [HttpPost("gym/images/{imageId:guid}/content")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(GymImage.MaximumFileSizeBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = GymImage.MaximumFileSizeBytes + 65536)]
    public async Task<ActionResult<GymImageGalleryDto>> ReplaceGymImage(
        Guid imageId,
        [FromForm] GymImageUploadForm form,
        CancellationToken cancellationToken) =>
        Ok(await gymImages.ReplaceAsync(
            imageId,
            await form.ToUploadAsync(cancellationToken),
            cancellationToken));

    [HttpDelete("gym/images/{imageId:guid}")]
    public async Task<ActionResult<GymImageGalleryDto>> RemoveGymImage(
        Guid imageId,
        GymImageMutationRequest request,
        CancellationToken cancellationToken) =>
        Ok(await gymImages.RemoveAsync(imageId, request, cancellationToken));

    [HttpPut("gym/images/order")]
    public async Task<ActionResult<GymImageGalleryDto>> ReorderGymImages(
        GymImageOrderRequest request,
        CancellationToken cancellationToken) =>
        Ok(await gymImages.ReorderAsync(request, cancellationToken));

    [HttpGet("trainers")]
    public async Task<IActionResult> SearchTrainers(
        [FromQuery] TrainerSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await trainers.SearchAsync(request, cancellationToken));

    [HttpGet("trainer-candidates")]
    public async Task<IActionResult> SearchTrainerCandidates(
        [FromQuery] TrainerCandidateSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await trainers.SearchCandidatesAsync(request, cancellationToken));

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

    [HttpPost("trainers/{id:guid}/deactivate")]
    public async Task<IActionResult> DeactivateTrainer(
        Guid id,
        TrainerLifecycleRequest request,
        CancellationToken cancellationToken) =>
        Ok(await trainers.DeactivateAsync(id, request, cancellationToken));

    [HttpPost("trainers/{id:guid}/reactivate")]
    public async Task<IActionResult> ReactivateTrainer(
        Guid id,
        TrainerLifecycleRequest request,
        CancellationToken cancellationToken) =>
        Ok(await trainers.ReactivateAsync(id, request, cancellationToken));

    [HttpPost("trainers/{id:guid}/image")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(TrainerProfile.MaximumImageFileSizeBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = TrainerProfile.MaximumImageFileSizeBytes + 65536)]
    public async Task<ActionResult<TrainerImageDto>> UploadTrainerImage(
        Guid id,
        [FromForm] TrainerImageUploadForm form,
        CancellationToken cancellationToken) =>
        Ok(await trainerImages.UploadForTenantAsync(
            id,
            await form.ToUploadAsync(cancellationToken),
            cancellationToken));

    [HttpDelete("trainers/{id:guid}/image")]
    public async Task<ActionResult<TrainerImageDto>> RemoveTrainerImage(
        Guid id,
        TrainerImageMutationRequest request,
        CancellationToken cancellationToken) =>
        Ok(await trainerImages.RemoveForTenantAsync(id, request, cancellationToken));

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

}
