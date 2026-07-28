using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Identity;
using GymLink.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Administration;

internal interface IGymAdminAssignmentCoordinator
{
    Task AssignAsync(
        Guid userId,
        Guid tenantId,
        string reason,
        Guid actorId,
        CancellationToken cancellationToken);
}

internal sealed class GymAdminAssignmentCoordinator(
    IApplicationDbContext dbContext,
    IIdentityAccountManager accounts,
    ITenantMutationScope tenantMutationScope,
    TimeProvider timeProvider) : IGymAdminAssignmentCoordinator
{
    public async Task AssignAsync(
        Guid userId,
        Guid tenantId,
        string reason,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        var tenant = await dbContext.Tenants.SingleOrDefaultAsync(
                x => x.Id == tenantId,
                cancellationToken)
            ?? throw new NotFoundException("tenant_not_found", "The tenant was not found.");
        if (tenant.Status is not (TenantStatus.PendingActivation or TenantStatus.Active))
        {
            throw new ConflictException(
                "tenant_unavailable",
                "The tenant status does not permit this staff assignment.");
        }

        var account = await accounts.FindByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("account_not_found", "The account was not found.");
        var profile = await dbContext.UserProfiles.SingleAsync(
            x => x.Id == userId,
            cancellationToken);
        if (!profile.IsActive)
        {
            throw new ConflictException(
                "gym_admin_candidate_invalid",
                "The selected account is not active.");
        }

        var activeAssignment = await dbContext.UserGymAssignments
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.UserId == userId && x.Status == AssignmentStatus.Active,
                cancellationToken);
        if (activeAssignment is not null &&
            activeAssignment.TenantId == tenantId &&
            activeAssignment.Role == RoleNames.GymAdmin)
        {
            return;
        }

        if (activeAssignment is not null)
        {
            throw new ConflictException(
                "gym_admin_already_assigned",
                "The selected account already has an active gym assignment. Revoke it before assigning another gym.");
        }

        if (await dbContext.UserGymAssignments.IgnoreQueryFilters().AnyAsync(
                x => x.TenantId == tenantId &&
                     x.Role == RoleNames.GymAdmin &&
                     x.Status == AssignmentStatus.Active,
                cancellationToken))
        {
            throw new ConflictException(
                "tenant_gym_admin_exists",
                "This gym already has an active GymAdmin.");
        }

        if (account.Role != RoleNames.Member)
        {
            throw new ConflictException(
                "gym_admin_candidate_invalid",
                "Only an active Member account can be assigned as GymAdmin.");
        }

        using var tenantWrite = tenantMutationScope.Begin(tenantId);
        var assignment = await dbContext.UserGymAssignments.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.UserId == userId &&
                     x.TenantId == tenantId &&
                     x.Role == RoleNames.GymAdmin,
                cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (assignment is null)
        {
            dbContext.UserGymAssignments.Add(new UserGymAssignment
            {
                TenantId = tenantId,
                UserId = userId,
                Role = RoleNames.GymAdmin,
                Status = AssignmentStatus.Active,
                StartsAtUtc = now,
                ApprovedByUserId = actorId,
                Reason = reason.Trim(),
            });
        }
        else
        {
            assignment.Status = AssignmentStatus.Active;
            assignment.StartsAtUtc = now;
            assignment.EndsAtUtc = null;
            assignment.ApprovedByUserId = actorId;
            assignment.Reason = reason.Trim();
        }

        EnsureSucceeded(await accounts.ReplaceRoleAsync(
            userId,
            RoleNames.GymAdmin,
            cancellationToken));
        var sessions = await dbContext.RefreshTokenSessions
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.RevokedAtUtc = now;
            session.RevocationReason = "role_changed";
        }

        profile.TokenVersion++;
        dbContext.SecurityAuditRecords.Add(new SecurityAuditRecord
        {
            ActorUserId = actorId,
            TargetUserId = userId,
            TargetTenantId = tenantId,
            Action = "user.role_assigned",
            TargetType = nameof(UserProfile),
            TargetId = userId,
            Reason = reason.Trim(),
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAtUtc = now,
        });
    }

    private static void EnsureSucceeded(IdentityOperationResult result)
    {
        if (!result.Succeeded)
        {
            throw new ConflictException("role_change_failed", string.Join(" ", result.Errors));
        }
    }
}
