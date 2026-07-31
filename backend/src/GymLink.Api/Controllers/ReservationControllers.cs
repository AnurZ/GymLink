using GymLink.Application.Authorization;
using GymLink.Application.Catalog;
using GymLink.Application.Reservations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Authorize(Policy = PolicyNames.TenantStaff)]
[Route("api/tenant/trainer-offerings")]
public sealed class TenantTrainerOfferingsController(
    ITrainerOfferingService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] CatalogSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.SearchAsync(request, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateTrainerOfferingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return Created($"/api/tenant/trainer-offerings/{result.Id}", result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateTrainerOfferingRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await service.DeactivateAsync(id, cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Authorize(Policy = PolicyNames.TenantStaff)]
[Route("api/tenant/trainer-availability")]
public sealed class TenantAvailabilityController(IAvailabilityService service) : ControllerBase
{
    [HttpGet("schedule")]
    public async Task<IActionResult> GetSchedule(
        [FromQuery] Guid trainerProfileId,
        CancellationToken cancellationToken) =>
        Ok(await service.GetScheduleAsync(trainerProfileId, cancellationToken));

    [HttpPut("schedule")]
    public async Task<IActionResult> ReplaceSchedule(
        ReplaceTrainerScheduleRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.ReplaceScheduleAsync(request, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] AvailabilitySearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.SearchTenantAsync(request, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return Created($"/api/tenant/trainer-availability/{result.Id}", result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateAvailabilityRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        Guid id,
        ReservationConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.CancelAsync(id, request, cancellationToken));
}

[ApiController]
[AllowAnonymous]
public sealed class PublicReservationCatalogController(
    IAvailabilityService availability,
    IReviewService reviews) : ControllerBase
{
    [HttpGet("api/trainers/{trainerId:guid}/availability")]
    public async Task<IActionResult> SearchAvailability(
        Guid trainerId,
        [FromQuery] PublicAvailabilitySearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await availability.SearchPublicAsync(trainerId, request, cancellationToken));

    [HttpGet("api/trainers/{trainerId:guid}/availability-calendar")]
    public async Task<IActionResult> GetAvailabilityCalendar(
        Guid trainerId,
        [FromQuery] PublicAvailabilityCalendarRequest request,
        CancellationToken cancellationToken) =>
        Ok(await availability.GetPublicCalendarAsync(trainerId, request, cancellationToken));

    [HttpGet("api/trainers/{trainerId:guid}/reviews")]
    public async Task<IActionResult> SearchTrainerReviews(
        Guid trainerId,
        [FromQuery] ReviewSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await reviews.SearchTrainerReviewsAsync(trainerId, request, cancellationToken));

    [HttpGet("api/gyms/{gymId:guid}/reviews")]
    public async Task<IActionResult> SearchGymReviews(
        Guid gymId,
        [FromQuery] ReviewSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await reviews.SearchGymReviewsAsync(gymId, request, cancellationToken));
}

[ApiController]
[Authorize(Policy = PolicyNames.MemberSelf)]
public sealed class MemberReservationCommandsController(
    IReservationService reservations,
    IReviewService reviews) : ControllerBase
{
    [HttpPost("api/reservations")]
    public async Task<IActionResult> Create(
        CreateReservationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await reservations.CreateAsync(request, cancellationToken);
        return Created($"/api/me/reservations/{result.Id}", result);
    }

    [HttpPost("api/reservations/{reservationId:guid}/review")]
    public async Task<IActionResult> CreateTrainerReview(
        Guid reservationId,
        CreateReviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await reviews.CreateTrainerReviewAsync(
            reservationId,
            request,
            cancellationToken);
        return Created($"/api/reservations/{reservationId}/review", result);
    }

    [HttpPost("api/gyms/{gymId:guid}/reviews")]
    public async Task<IActionResult> CreateGymReview(
        Guid gymId,
        CreateReviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await reviews.CreateGymReviewAsync(gymId, request, cancellationToken);
        return Created($"/api/gyms/{gymId}/reviews", result);
    }
}

[ApiController]
[Authorize(Policy = PolicyNames.MemberSelf)]
[Route("api/me/reservations")]
public sealed class MyReservationsController(IReservationService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] ReservationSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.SearchMineAsync(request, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetMineAsync(id, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        Guid id,
        ReservationConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.CancelMineAsync(id, request, cancellationToken));
}

[ApiController]
[Authorize(Policy = PolicyNames.TenantTrainer)]
[Route("api/me/trainer-reservations")]
public sealed class MyTrainerReservationsController(IReservationService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] ReservationSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.SearchTrainerAsync(request, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetTrainerAsync(id, cancellationToken));
}

[ApiController]
[Authorize(Policy = PolicyNames.TenantStaff)]
[Route("api/tenant/reservations")]
public sealed class TenantReservationsController(IReservationService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] ReservationSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.SearchTenantAsync(request, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetTenantAsync(id, cancellationToken));

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(
        Guid id,
        ReservationConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.ConfirmAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        Guid id,
        StaffCancellationRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.CancelStaffAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(
        Guid id,
        ReservationConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.CompleteAsync(id, request, cancellationToken));
}
