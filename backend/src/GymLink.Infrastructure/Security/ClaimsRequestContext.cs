using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GymLink.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace GymLink.Infrastructure.Security;

internal sealed class ClaimsRequestContext(IHttpContextAccessor accessor) : ICurrentUser, ITenantContext
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(
            Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier),
            out var id)
            ? id
            : null;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? TenantId =>
        Guid.TryParse(Principal?.FindFirstValue("tenant_id"), out var id) ? id : null;

    public string? TenantRole => Principal?.FindFirstValue("tenant_role");

    public bool HasTenant => TenantId.HasValue;
}
