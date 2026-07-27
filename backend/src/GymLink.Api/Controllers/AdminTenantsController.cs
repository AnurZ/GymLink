using GymLink.Application.Administration;
using GymLink.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Authorize(Policy = PolicyNames.CentralAdminOnly)]
[Route("api/admin/tenants")]
public sealed class AdminTenantsController(
    ITenantAdministrationService tenantAdministration) : ControllerBase
{
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        Ok(await tenantAdministration.ActivateAsync(id, cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(
        Guid id,
        TenantStatusReasonRequest request,
        CancellationToken cancellationToken) =>
        Ok(await tenantAdministration.DeactivateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/suspend")]
    public async Task<IActionResult> Suspend(
        Guid id,
        TenantStatusReasonRequest request,
        CancellationToken cancellationToken) =>
        Ok(await tenantAdministration.SuspendAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(
        Guid id,
        TenantStatusReasonRequest request,
        CancellationToken cancellationToken) =>
        Ok(await tenantAdministration.ReactivateAsync(id, request, cancellationToken));
}
