using System.ComponentModel.DataAnnotations;
using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Identity;
using GymLink.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Administration;

public sealed record UserSearchRequest : PagedRequest
{
    [MaxLength(320)]
    public string? Query { get; init; }

    [MaxLength(32)]
    public string? Role { get; init; }

    public bool? IsActive { get; init; }
}

public sealed record RoleAssignmentRequest
{
    [Required, MaxLength(320)]
    public required string Identifier { get; init; }

    [Required, MaxLength(32)]
    public required string Role { get; init; }

    public Guid? TenantId { get; init; }

    [Required, StringLength(1000, MinimumLength = 2)]
    public required string Reason { get; init; }
}

public sealed record UserActionRequest
{
    [Required, MaxLength(320)]
    public required string Identifier { get; init; }

    [Required, StringLength(1000, MinimumLength = 2)]
    public required string Reason { get; init; }
}

public sealed record AdminUserDto(
    Guid Id,
    string Username,
    string Email,
    string DisplayName,
    string? PhoneNumber,
    string Role,
    bool IsActive,
    TenantSessionDto? Assignment);

public interface IUserAdministrationService
{
    Task<PagedResult<AdminUserDto>> SearchAsync(
        UserSearchRequest request,
        CancellationToken cancellationToken);
    Task<AdminUserDto> GetAsync(string identifier, CancellationToken cancellationToken);
    Task<AdminUserDto> AssignRoleAsync(
        RoleAssignmentRequest request,
        CancellationToken cancellationToken);
    Task<AdminUserDto> RevokeRoleAsync(
        UserActionRequest request,
        CancellationToken cancellationToken);
    Task<AdminUserDto> DeactivateAsync(
        UserActionRequest request,
        CancellationToken cancellationToken);
    Task<AdminUserDto> ReactivateAsync(
        UserActionRequest request,
        CancellationToken cancellationToken);
}

internal sealed class UserAdministrationService(
    IApplicationDbContext dbContext,
    IIdentityAccountManager accounts,
    IApplicationTransaction transaction,
    ICurrentUser currentUser,
    ITenantMutationScope tenantMutationScope,
    IGymAdminAssignmentCoordinator gymAdminAssignment,
    TimeProvider timeProvider) : IUserAdministrationService
{
    public async Task<PagedResult<AdminUserDto>> SearchAsync(
        UserSearchRequest request,
        CancellationToken cancellationToken)
    {
        request.Validate();
        if (!string.IsNullOrWhiteSpace(request.Role) &&
            !RoleNames.All.Contains(request.Role.Trim()))
        {
            throw new ApplicationRuleException(
                "role_invalid",
                "The requested role filter is not supported.");
        }

        var (items, totalCount) = await accounts.SearchAsync(
            request.Query,
            request.Role?.Trim(),
            request.IsActive,
            (request.Page - 1) * request.PageSize,
            request.PageSize,
            cancellationToken);
        var results = new List<AdminUserDto>(items.Count);
        foreach (var account in items)
        {
            results.Add(await BuildAsync(account, cancellationToken));
        }

        return new(results, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminUserDto> GetAsync(
        string identifier,
        CancellationToken cancellationToken)
    {
        var account = await FindRequiredAsync(identifier, cancellationToken);
        return await BuildAsync(account, cancellationToken);
    }

    public async Task<AdminUserDto> AssignRoleAsync(
        RoleAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteSerializableAsync(async token =>
            {
                if (request.Role == RoleNames.CentralAdmin)
                {
                    throw new ConflictException(
                        "central_admin_fixed",
                        "The seeded CentralAdmin is the only CentralAdmin account.");
                }

                if (!RoleNames.All.Contains(request.Role))
                {
                    throw new ApplicationRuleException(
                        "role_invalid",
                        "The requested role is not supported.");
                }

                var actorId = RequireActor();
                var account = await FindRequiredAsync(request.Identifier, token);
                if (account.Role == RoleNames.CentralAdmin &&
                    request.Role != RoleNames.CentralAdmin)
                {
                    await EnsureNotLastCentralAdminAsync(token);
                }

                Tenant? tenant = null;
                if (request.Role is RoleNames.GymAdmin or RoleNames.Trainer)
                {
                    if (!request.TenantId.HasValue)
                    {
                        throw new ApplicationRuleException(
                            "tenant_required",
                            "A gym tenant is required for staff roles.");
                    }

                    tenant = await dbContext.Tenants
                        .SingleOrDefaultAsync(x => x.Id == request.TenantId, token)
                        ?? throw new NotFoundException(
                            "tenant_not_found",
                            "The tenant was not found.");
                    var allowed = request.Role == RoleNames.GymAdmin
                        ? tenant.Status is TenantStatus.PendingActivation or TenantStatus.Active
                        : tenant.Status == TenantStatus.Active;
                    if (!allowed)
                    {
                        throw new ConflictException(
                            "tenant_unavailable",
                            "The tenant status does not permit this staff assignment.");
                    }
                }
                else if (request.TenantId.HasValue)
                {
                    throw new ApplicationRuleException(
                        "tenant_not_allowed",
                        "Member and CentralAdmin roles cannot have a tenant assignment.");
                }

                if (request.Role == RoleNames.GymAdmin && tenant is not null)
                {
                    await gymAdminAssignment.AssignAsync(
                        account.Id,
                        tenant.Id,
                        request.Reason,
                        actorId,
                        token);
                    await dbContext.SaveChangesAsync(token);
                    return await BuildAsync(
                        await FindRequiredAsync(account.Id.ToString(), token),
                        token);
                }

                using var tenantWrite =
                    await BeginAssignmentMutationAsync(account.Id, tenant?.Id, token);
                await EndActiveStaffAssignmentsAsync(account.Id, request.Reason, token);
                if (tenant is not null)
                {
                    var assignment = await dbContext.UserGymAssignments
                        .IgnoreQueryFilters()
                        .SingleOrDefaultAsync(
                            x => x.UserId == account.Id &&
                                 x.TenantId == tenant.Id &&
                                 x.Role == request.Role,
                            token);
                    if (assignment is null)
                    {
                        dbContext.UserGymAssignments.Add(new UserGymAssignment
                        {
                            TenantId = tenant.Id,
                            UserId = account.Id,
                            Role = request.Role,
                            Status = AssignmentStatus.Active,
                            StartsAtUtc = timeProvider.GetUtcNow().UtcDateTime,
                            ApprovedByUserId = actorId,
                            Reason = request.Reason.Trim(),
                        });
                    }
                    else
                    {
                        assignment.Status = AssignmentStatus.Active;
                        assignment.StartsAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                        assignment.EndsAtUtc = null;
                        assignment.ApprovedByUserId = actorId;
                        assignment.Reason = request.Reason.Trim();
                    }
                }

                EnsureSucceeded(await accounts.ReplaceRoleAsync(account.Id, request.Role, token));
                await RevokeSessionsAsync(account.Id, "role_changed", token);
                AddAudit(actorId, account.Id, tenant?.Id, "user.role_assigned", request.Reason);
                await dbContext.SaveChangesAsync(token);
                return await BuildAsync(
                    await FindRequiredAsync(account.Id.ToString(), token),
                    token);
            }, cancellationToken);
        }
        catch (Exception exception) when (ContainsDbUpdateException(exception))
        {
            throw await ResolveGymAdminConflictAsync(request, exception, cancellationToken);
        }
    }

    public Task<AdminUserDto> RevokeRoleAsync(
        UserActionRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async token =>
        {
            var actorId = RequireActor();
            var account = await FindRequiredAsync(request.Identifier, token);
            if (account.Role == RoleNames.Member)
            {
                throw new ConflictException("role_not_revocable", "The Member role cannot be revoked.");
            }

            if (account.Role == RoleNames.CentralAdmin)
            {
                await EnsureNotLastCentralAdminAsync(token);
            }

            using var tenantWrite = await BeginAssignmentMutationAsync(account.Id, null, token);
            await EndActiveStaffAssignmentsAsync(account.Id, request.Reason, token);
            EnsureSucceeded(await accounts.ReplaceRoleAsync(account.Id, RoleNames.Member, token));
            await RevokeSessionsAsync(account.Id, "role_revoked", token);
            AddAudit(actorId, account.Id, null, "user.role_revoked", request.Reason);
            await dbContext.SaveChangesAsync(token);
            return await BuildAsync(
                await FindRequiredAsync(account.Id.ToString(), token),
                token);
        }, cancellationToken);

    public Task<AdminUserDto> DeactivateAsync(
        UserActionRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async token =>
        {
            var actorId = RequireActor();
            var account = await FindRequiredAsync(request.Identifier, token);
            if (account.Id == actorId)
            {
                throw new ConflictException(
                    "self_deactivation_forbidden",
                    "Central administrators cannot deactivate their own account.");
            }

            if (account.Role == RoleNames.CentralAdmin)
            {
                await EnsureNotLastCentralAdminAsync(token);
            }

            var profile = await dbContext.UserProfiles.SingleAsync(x => x.Id == account.Id, token);
            if (profile.IsActive)
            {
                profile.IsActive = false;
                using var tenantWrite = await BeginAssignmentMutationAsync(account.Id, null, token);
                await EndActiveStaffAssignmentsAsync(account.Id, request.Reason, token);
                await RevokeSessionsAsync(account.Id, "account_deactivated", token);
                AddAudit(actorId, account.Id, null, "user.deactivated", request.Reason);
                await dbContext.SaveChangesAsync(token);
            }

            return await BuildAsync(account, token);
        }, cancellationToken);

    public Task<AdminUserDto> ReactivateAsync(
        UserActionRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async token =>
        {
            var actorId = RequireActor();
            var account = await FindRequiredAsync(request.Identifier, token);
            var profile = await dbContext.UserProfiles.SingleAsync(x => x.Id == account.Id, token);
            if (!profile.IsActive)
            {
                profile.IsActive = true;
                profile.TokenVersion++;
                AddAudit(actorId, account.Id, null, "user.reactivated", request.Reason);
                await dbContext.SaveChangesAsync(token);
            }

            return await BuildAsync(account, token);
        }, cancellationToken);

    private async Task<AdminUserDto> BuildAsync(
        IdentityAccount account,
        CancellationToken cancellationToken)
    {
        var profile = await dbContext.UserProfiles.AsNoTracking()
            .SingleAsync(x => x.Id == account.Id, cancellationToken);
        TenantSessionDto? assignment = null;
        if (account.Role is RoleNames.GymAdmin or RoleNames.Trainer)
        {
            assignment = await (
                    from item in dbContext.UserGymAssignments.IgnoreQueryFilters().AsNoTracking()
                    join tenant in dbContext.Tenants.AsNoTracking() on item.TenantId equals tenant.Id
                    where item.UserId == account.Id &&
                          item.Role == account.Role &&
                          item.Status == AssignmentStatus.Active
                    select new TenantSessionDto(item.TenantId, tenant.Name, item.Role))
                .SingleOrDefaultAsync(cancellationToken);
        }

        return new(
            account.Id,
            account.Username,
            account.Email,
            profile.DisplayName,
            profile.PhoneNumber,
            account.Role,
            profile.IsActive,
            assignment);
    }

    private async Task<IdentityAccount> FindRequiredAsync(
        string identifier,
        CancellationToken cancellationToken)
    {
        if (Guid.TryParse(identifier, out var id))
        {
            return await accounts.FindByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException("account_not_found", "The account was not found.");
        }

        return await accounts.FindByIdentifierAsync(identifier.Trim(), cancellationToken)
            ?? throw new NotFoundException("account_not_found", "The account was not found.");
    }

    private async Task EndActiveStaffAssignmentsAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken)
    {
        var assignments = await dbContext.UserGymAssignments
            .IgnoreQueryFilters()
            .Where(x => x.UserId == userId && x.Status == AssignmentStatus.Active)
            .ToListAsync(cancellationToken);
        foreach (var assignment in assignments)
        {
            assignment.Status = AssignmentStatus.Ended;
            assignment.EndsAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            assignment.Reason = reason.Trim();
        }
    }

    private async Task<IDisposable?> BeginAssignmentMutationAsync(
        Guid userId,
        Guid? additionalTenantId,
        CancellationToken cancellationToken)
    {
        var tenantIds = await dbContext.UserGymAssignments
            .IgnoreQueryFilters()
            .Where(x => x.UserId == userId && x.Status == AssignmentStatus.Active)
            .Select(x => x.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (additionalTenantId.HasValue)
        {
            tenantIds.Add(additionalTenantId.Value);
        }

        return tenantIds.Count == 0
            ? null
            : tenantMutationScope.Begin([.. tenantIds.Distinct()]);
    }

    private async Task RevokeSessionsAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken)
    {
        var profile = await dbContext.UserProfiles.SingleAsync(x => x.Id == userId, cancellationToken);
        var sessions = await dbContext.RefreshTokenSessions
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var session in sessions)
        {
            session.RevokedAtUtc = now;
            session.RevocationReason = reason;
        }

        profile.TokenVersion++;
    }

    private async Task EnsureNotLastCentralAdminAsync(CancellationToken cancellationToken)
    {
        if (await accounts.CountInRoleAsync(RoleNames.CentralAdmin, cancellationToken) <= 1)
        {
            throw new ConflictException(
                "last_central_admin",
                "The last active CentralAdmin cannot be removed or deactivated.");
        }
    }

    private async Task<ConflictException> ResolveGymAdminConflictAsync(
        RoleAssignmentRequest request,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (request.Role == RoleNames.GymAdmin && request.TenantId.HasValue)
        {
            if (await dbContext.UserGymAssignments.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(
                        x => x.TenantId == request.TenantId.Value &&
                             x.Role == RoleNames.GymAdmin &&
                             x.Status == AssignmentStatus.Active,
                        cancellationToken))
            {
                return new ConflictException(
                    "tenant_gym_admin_exists",
                    "This gym already has an active GymAdmin.",
                    exception);
            }

            var account = await FindRequiredAsync(request.Identifier, cancellationToken);
            if (await dbContext.UserGymAssignments.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(
                        x => x.UserId == account.Id &&
                             x.Status == AssignmentStatus.Active,
                        cancellationToken))
            {
                return new ConflictException(
                    "gym_admin_already_assigned",
                    "The selected account already has an active gym assignment. Revoke it before assigning another gym.",
                    exception);
            }
        }

        return new ConflictException(
            "role_change_failed",
            "The role assignment conflicted with another change. Reload and try again.",
            exception);
    }

    private static bool ContainsDbUpdateException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is DbUpdateException)
            {
                return true;
            }
        }

        return false;
    }

    private void AddAudit(
        Guid actorId,
        Guid targetId,
        Guid? tenantId,
        string action,
        string reason) =>
        dbContext.SecurityAuditRecords.Add(new SecurityAuditRecord
        {
            ActorUserId = actorId,
            TargetUserId = targetId,
            TargetTenantId = tenantId,
            Action = action,
            TargetType = nameof(UserProfile),
            TargetId = targetId,
            Reason = reason.Trim(),
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        });

    private Guid RequireActor() =>
        currentUser.UserId
        ?? throw new AuthenticationFailedException("authentication_required", "Authentication is required.");

    private static void EnsureSucceeded(IdentityOperationResult result)
    {
        if (!result.Succeeded)
        {
            throw new ConflictException("role_change_failed", string.Join(" ", result.Errors));
        }
    }
}
