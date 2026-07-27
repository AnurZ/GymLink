using GymLink.Application.Authorization;
using GymLink.Application.Memberships;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Authorize(Policy = PolicyNames.MemberSelf)]
[Route("api/membership-requests")]
public sealed class MembershipRequestsController(
    IMembershipRequestService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<MembershipRequestDto>> Create(
        CreateMembershipRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return Created($"/api/me/membership-requests/{result.Id}", result);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        Guid id,
        ConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.CancelMineAsync(id, request, cancellationToken));
}

[ApiController]
[Authorize(Policy = PolicyNames.MemberSelf)]
[Route("api/me/membership-requests")]
public sealed class MyMembershipRequestsController(
    IMembershipRequestService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] MembershipRequestSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.SearchMineAsync(request, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetMineAsync(id, cancellationToken));
}

[ApiController]
[Authorize(Policy = PolicyNames.TenantGymAdmin)]
[Route("api/tenant/membership-requests")]
public sealed class TenantMembershipRequestsController(
    IMembershipRequestService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] MembershipRequestSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.SearchTenantAsync(request, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetTenantAsync(id, cancellationToken));

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(
        Guid id,
        ConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.ApproveAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(
        Guid id,
        ReasonedConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.RejectAsync(id, request, cancellationToken));
}

[ApiController]
[Authorize(Policy = PolicyNames.MemberSelf)]
[Route("api/me/memberships")]
public sealed class MyMembershipsController(IMembershipService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] MembershipSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.SearchMineAsync(request, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetMineAsync(id, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        Guid id,
        ConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.CancelMineAsync(id, request, cancellationToken));
}

[ApiController]
[Authorize(Policy = PolicyNames.TenantGymAdmin)]
[Route("api/tenant/memberships")]
public sealed class TenantMembershipsController(IMembershipService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] MembershipSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.SearchTenantAsync(request, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetTenantAsync(id, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        Guid id,
        ReasonedConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.CancelAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/suspend")]
    public async Task<IActionResult> Suspend(
        Guid id,
        ReasonedConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.SuspendAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(
        Guid id,
        ReasonedConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.ReactivateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/expire")]
    public async Task<IActionResult> Expire(
        Guid id,
        ConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.ExpireAsync(id, request, cancellationToken));
}
