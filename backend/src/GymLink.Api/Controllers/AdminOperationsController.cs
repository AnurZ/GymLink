using GymLink.Application.Authorization;
using GymLink.Application.Memberships;
using GymLink.Application.Reservations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Authorize(Policy = PolicyNames.CentralAdminOnly)]
[Route("api/admin/gyms/{gymId:guid}")]
public sealed class AdminOperationsController(
    IMembershipRequestService memberships,
    IReservationService reservations) : ControllerBase
{
    [HttpGet("membership-requests")]
    public async Task<IActionResult> SearchMembershipRequests(
        Guid gymId,
        [FromQuery] MembershipRequestSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await memberships.SearchAdminGymAsync(gymId, request, cancellationToken));

    [HttpPost("membership-requests/{requestId:guid}/confirm-cash")]
    public async Task<IActionResult> ConfirmCash(
        Guid gymId,
        Guid requestId,
        ConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await memberships.ConfirmCashAdminGymAsync(
            gymId,
            requestId,
            request,
            cancellationToken));

    [HttpGet("reservations")]
    public async Task<IActionResult> SearchReservations(
        Guid gymId,
        [FromQuery] ReservationSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await reservations.SearchAdminGymAsync(gymId, request, cancellationToken));

    [HttpPost("reservations/{reservationId:guid}/complete")]
    public async Task<IActionResult> CompleteReservation(
        Guid gymId,
        Guid reservationId,
        ReservationConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await reservations.CompleteAdminGymAsync(
            gymId,
            reservationId,
            request,
            cancellationToken));
}
