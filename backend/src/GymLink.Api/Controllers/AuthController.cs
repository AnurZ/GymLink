using GymLink.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthenticationService authenticationService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthSessionDto>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken) =>
        StatusCode(
            StatusCodes.Status201Created,
            await authenticationService.RegisterAsync(request, cancellationToken));

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthSessionDto>> Login(
        LoginRequest request,
        CancellationToken cancellationToken) =>
        Ok(await authenticationService.LoginAsync(request, cancellationToken));

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthSessionDto>> Refresh(
        RefreshSessionRequest request,
        CancellationToken cancellationToken) =>
        Ok(await authenticationService.RefreshAsync(request, cancellationToken));

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        LogoutRequest request,
        CancellationToken cancellationToken)
    {
        await authenticationService.LogoutAsync(request, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        await authenticationService.LogoutAllAsync(cancellationToken);
        return NoContent();
    }
}
