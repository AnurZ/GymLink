using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Infrastructure.Security;

internal sealed class JwtTokenValidationEvents(
    GymLinkDbContext dbContext,
    TimeProvider timeProvider) : JwtBearerEvents
{
    public override async Task Challenge(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "authentication_required",
                Detail = "A valid bearer token is required.",
                Extensions = { ["traceId"] = context.HttpContext.TraceIdentifier },
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.HttpContext.RequestAborted);
    }

    public override async Task Forbidden(ForbiddenContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "access_denied",
                Detail = "You are not authorized to perform this action.",
                Extensions = { ["traceId"] = context.HttpContext.TraceIdentifier },
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.HttpContext.RequestAborted);
    }

    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal is null ||
            !Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId) ||
            !Guid.TryParse(principal.FindFirstValue("sid"), out var sessionId) ||
            !int.TryParse(principal.FindFirstValue("token_version"), out var tokenVersion))
        {
            context.Fail("Required token claims are missing.");
            return;
        }

        var roleClaims = principal.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray();
        if (roleClaims.Length != 1 || !RoleNames.All.Contains(roleClaims[0]))
        {
            context.Fail("The token must contain exactly one supported role.");
            return;
        }

        var profile = await dbContext.UserProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, context.HttpContext.RequestAborted);
        var session = await dbContext.RefreshTokenSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == sessionId, context.HttpContext.RequestAborted);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (profile is null ||
            !profile.IsActive ||
            profile.TokenVersion != tokenVersion ||
            session is null ||
            session.UserId != userId ||
            session.RevokedAtUtc is not null ||
            session.ExpiresAtUtc <= now ||
            session.Jti != principal.FindFirstValue(JwtRegisteredClaimNames.Jti))
        {
            context.Fail("The session is no longer valid.");
            return;
        }

        var role = roleClaims[0];
        var tenantClaim = principal.FindFirstValue("tenant_id");
        var tenantRole = principal.FindFirstValue("tenant_role");
        if (role is RoleNames.GymAdmin or RoleNames.Trainer)
        {
            if (!Guid.TryParse(tenantClaim, out var tenantId) || tenantRole != role)
            {
                context.Fail("The staff tenant claims are invalid.");
                return;
            }

            var assignment = await dbContext.UserGymAssignments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.UserId == userId &&
                         x.TenantId == tenantId &&
                         x.Role == role &&
                         x.Status == AssignmentStatus.Active,
                    context.HttpContext.RequestAborted);
            var tenant = await dbContext.Tenants
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == tenantId, context.HttpContext.RequestAborted);
            var tenantAllowed = tenant is not null &&
                (role == RoleNames.GymAdmin
                    ? tenant.Status is TenantStatus.PendingActivation or TenantStatus.Active
                    : tenant.Status == TenantStatus.Active);
            if (assignment is null || !tenantAllowed)
            {
                context.Fail("The staff assignment is no longer active.");
            }
        }
        else if (tenantClaim is not null || tenantRole is not null)
        {
            context.Fail("This role cannot carry tenant claims.");
        }
    }
}
