using GymLink.Application.Administration;
using GymLink.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Authorize(Policy = PolicyNames.CentralAdminOnly)]
[Route("api/admin/users")]
public sealed class AdminUsersController(
    IUserAdministrationService userAdministration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] UserSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await userAdministration.SearchAsync(request, cancellationToken));

    [HttpGet("{identifier}")]
    public async Task<IActionResult> Get(
        string identifier,
        CancellationToken cancellationToken) =>
        Ok(await userAdministration.GetAsync(identifier, cancellationToken));

    [HttpPost("roles/assign")]
    public async Task<IActionResult> AssignRole(
        RoleAssignmentRequest request,
        CancellationToken cancellationToken) =>
        Ok(await userAdministration.AssignRoleAsync(request, cancellationToken));

    [HttpPost("roles/revoke")]
    public async Task<IActionResult> RevokeRole(
        UserActionRequest request,
        CancellationToken cancellationToken) =>
        Ok(await userAdministration.RevokeRoleAsync(request, cancellationToken));

    [HttpPost("deactivate")]
    public async Task<IActionResult> Deactivate(
        UserActionRequest request,
        CancellationToken cancellationToken) =>
        Ok(await userAdministration.DeactivateAsync(request, cancellationToken));

    [HttpPost("reactivate")]
    public async Task<IActionResult> Reactivate(
        UserActionRequest request,
        CancellationToken cancellationToken) =>
        Ok(await userAdministration.ReactivateAsync(request, cancellationToken));
}
