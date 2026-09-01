using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Identity;
using GymLink.Domain.Memberships;
using GymLink.Domain.Tenancy;
using GymLink.Domain.Trainers;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Catalog;

internal enum TrainerAdministrationAction
{
    RoleAssignmentToMember,
    RoleRevocation,
    AccountDeactivation,
}

internal sealed record TrainerLifecycleResult(
    TrainerProfile Profile,
    UserProfile User,
    IReadOnlyList<Guid> TrainingTypeIds);

internal interface ITrainerLifecycleCoordinator
{
    Task<TrainerLifecycleResult> PromoteAsync(
        Guid tenantId,
        CreateTrainerRequest request,
        IReadOnlyList<Guid> trainingTypeIds,
        Guid actorId,
        CancellationToken cancellationToken);

    Task<TrainerLifecycleResult> DeactivateAsync(
        Guid tenantId,
        Guid trainerProfileId,
        string reason,
        Guid actorId,
        CancellationToken cancellationToken);

    Task<TrainerLifecycleResult> ReactivateAsync(
        Guid tenantId,
        Guid trainerProfileId,
        string reason,
        Guid actorId,
        CancellationToken cancellationToken);

    Task DeactivateForAdministrationAsync(
        Guid userId,
        string reason,
        Guid actorId,
        TrainerAdministrationAction action,
        CancellationToken cancellationToken);
}

internal sealed class TrainerLifecycleCoordinator(
    IApplicationDbContext dbContext,
    IIdentityAccountManager accounts,
    IApplicationTransaction transaction,
    ITenantMutationScope tenantMutationScope,
    TimeProvider timeProvider) : ITrainerLifecycleCoordinator
{
    public Task<TrainerLifecycleResult> PromoteAsync(
        Guid tenantId,
        CreateTrainerRequest request,
        IReadOnlyList<Guid> trainingTypeIds,
        Guid actorId,
        CancellationToken cancellationToken) =>
        transaction.ExecuteSerializableAsync(async token =>
        {
            var reason = NormalizeReason(request.Reason);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            await RequireActiveTenantAsync(tenantId, token);
            var user = await dbContext.UserProfiles
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == request.UserId && candidate.IsActive,
                    token)
                ?? throw new NotFoundException(
                    "trainer_candidate_not_found",
                    "The selected active member was not found.");
            var account = await accounts.FindByIdAsync(request.UserId, token)
                ?? throw new NotFoundException(
                    "trainer_candidate_not_found",
                    "The selected active member was not found.");
            if (account.Role != RoleNames.Member)
            {
                throw new ConflictException(
                    "trainer_candidate_invalid",
                    "Only an active Member account can be promoted to Trainer.");
            }

            if (!await dbContext.Memberships.AnyAsync(
                    membership =>
                        membership.MemberUserId == request.UserId &&
                        membership.Status == MembershipStatus.Active &&
                        membership.EndsAtUtc.HasValue &&
                        membership.EndsAtUtc > now,
                    token))
            {
                throw new ConflictException(
                    "active_membership_required",
                    "The selected member must have an active membership in this gym.");
            }

            if (await dbContext.TrainerProfiles.IgnoreQueryFilters()
                    .AnyAsync(trainer => trainer.UserId == request.UserId, token))
            {
                throw new ConflictException(
                    "trainer_duplicate",
                    "This user already has a trainer profile.");
            }

            var assignments = await ActiveAssignmentsAsync(request.UserId, token);
            if (assignments.Any(assignment =>
                    assignment.TenantId != tenantId ||
                    assignment.Role != RoleNames.Member))
            {
                throw LifecycleConflict(
                    "The selected member already has a conflicting active gym assignment.");
            }

            using var tenantWrite = tenantMutationScope.Begin(tenantId);
            EndAssignments(assignments, reason, now);
            await ActivateAssignmentAsync(
                request.UserId,
                tenantId,
                actorId,
                reason,
                now,
                token);

            var trainer = new TrainerProfile
            {
                TenantId = tenantId,
                UserId = request.UserId,
                Biography = request.Biography.Trim(),
                Credentials = request.Credentials?.Trim(),
            };
            dbContext.TrainerProfiles.Add(trainer);
            dbContext.TrainerTrainingTypes.AddRange(trainingTypeIds.Select(id =>
                new TrainerTrainingType
                {
                    TenantId = tenantId,
                    TrainerProfileId = trainer.Id,
                    TrainingTypeId = id,
                }));

            EnsureRoleChangeSucceeded(await accounts.ReplaceRoleAsync(
                request.UserId,
                RoleNames.Trainer,
                token));
            await RevokeSessionsAsync(user, "role_changed", now, token);
            AddLifecycleAudit(
                actorId,
                request.UserId,
                tenantId,
                trainer.Id,
                "trainer.promoted",
                reason,
                now);
            await dbContext.SaveChangesAsync(token);
            return new TrainerLifecycleResult(trainer, user, trainingTypeIds);
        }, cancellationToken);

    public Task<TrainerLifecycleResult> DeactivateAsync(
        Guid tenantId,
        Guid trainerProfileId,
        string reason,
        Guid actorId,
        CancellationToken cancellationToken) =>
        transaction.ExecuteSerializableAsync(async token =>
        {
            var normalizedReason = NormalizeReason(reason);
            var trainer = await dbContext.TrainerProfiles.IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    profile => profile.Id == trainerProfileId && profile.TenantId == tenantId,
                    token)
                ?? throw new NotFoundException("trainer_not_found", "The trainer was not found.");
            var user = await dbContext.UserProfiles.SingleAsync(
                profile => profile.Id == trainer.UserId,
                token);
            var account = await accounts.FindByIdAsync(trainer.UserId, token)
                ?? throw new NotFoundException("account_not_found", "The account was not found.");
            var assignments = await ActiveAssignmentsAsync(trainer.UserId, token);
            EnsureDeactivationIsCompatible(account, trainer, assignments);

            if (!trainer.IsActive &&
                account.Role == RoleNames.Member &&
                assignments.All(assignment => assignment.Role != RoleNames.Trainer))
            {
                return await BuildResultAsync(trainer, user, token);
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            using var tenantWrite = tenantMutationScope.Begin(tenantId);
            trainer.IsActive = false;
            EndAssignments(
                assignments.Where(assignment => assignment.Role == RoleNames.Trainer),
                normalizedReason,
                now);
            if (account.Role == RoleNames.Trainer)
            {
                EnsureRoleChangeSucceeded(await accounts.ReplaceRoleAsync(
                    trainer.UserId,
                    RoleNames.Member,
                    token));
            }

            await RevokeSessionsAsync(user, "trainer_deactivated", now, token);
            AddLifecycleAudit(
                actorId,
                trainer.UserId,
                tenantId,
                trainer.Id,
                "trainer.deactivated",
                normalizedReason,
                now);
            await dbContext.SaveChangesAsync(token);
            return await BuildResultAsync(trainer, user, token);
        }, cancellationToken);

    public Task<TrainerLifecycleResult> ReactivateAsync(
        Guid tenantId,
        Guid trainerProfileId,
        string reason,
        Guid actorId,
        CancellationToken cancellationToken) =>
        transaction.ExecuteSerializableAsync(async token =>
        {
            var normalizedReason = NormalizeReason(reason);
            await RequireActiveTenantAsync(tenantId, token);
            var trainer = await dbContext.TrainerProfiles.IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    profile => profile.Id == trainerProfileId && profile.TenantId == tenantId,
                    token)
                ?? throw new NotFoundException("trainer_not_found", "The trainer was not found.");
            var user = await dbContext.UserProfiles.SingleAsync(
                profile => profile.Id == trainer.UserId,
                token);
            if (!user.IsActive)
            {
                throw new ConflictException(
                    "trainer_account_inactive",
                    "The account must be active before the Trainer profile can be reactivated.");
            }

            var account = await accounts.FindByIdAsync(trainer.UserId, token)
                ?? throw new NotFoundException("account_not_found", "The account was not found.");
            var assignments = await ActiveAssignmentsAsync(trainer.UserId, token);
            if (trainer.IsActive && IsCanonicalActive(account, trainer, assignments))
            {
                return await BuildResultAsync(trainer, user, token);
            }

            if (trainer.IsActive || account.Role != RoleNames.Member)
            {
                throw LifecycleConflict(
                    "The Trainer profile cannot be reactivated because its account role is inconsistent.");
            }

            if (await dbContext.TrainerProfiles.IgnoreQueryFilters().AnyAsync(
                    profile => profile.UserId == trainer.UserId && profile.Id != trainer.Id,
                    token) ||
                assignments.Any(assignment =>
                    assignment.TenantId != tenantId ||
                    assignment.Role != RoleNames.Member))
            {
                throw LifecycleConflict(
                    "The Trainer profile cannot be reactivated because the user has a conflicting assignment.");
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            using var tenantWrite = tenantMutationScope.Begin(tenantId);
            EndAssignments(assignments, normalizedReason, now);
            await ActivateAssignmentAsync(
                trainer.UserId,
                tenantId,
                actorId,
                normalizedReason,
                now,
                token);
            trainer.IsActive = true;
            EnsureRoleChangeSucceeded(await accounts.ReplaceRoleAsync(
                trainer.UserId,
                RoleNames.Trainer,
                token));
            await RevokeSessionsAsync(user, "trainer_reactivated", now, token);
            AddLifecycleAudit(
                actorId,
                trainer.UserId,
                tenantId,
                trainer.Id,
                "trainer.reactivated",
                normalizedReason,
                now);
            await dbContext.SaveChangesAsync(token);
            return await BuildResultAsync(trainer, user, token);
        }, cancellationToken);

    public async Task DeactivateForAdministrationAsync(
        Guid userId,
        string reason,
        Guid actorId,
        TrainerAdministrationAction action,
        CancellationToken cancellationToken)
    {
        await transaction.ExecuteSerializableAsync(async token =>
        {
            var normalizedReason = NormalizeReason(reason);
            var account = await accounts.FindByIdAsync(userId, token)
                ?? throw new NotFoundException("account_not_found", "The account was not found.");
            if (account.Role != RoleNames.Trainer)
            {
                throw LifecycleConflict(
                    "Trainer lifecycle administration requires the Trainer Identity role.");
            }

            var user = await dbContext.UserProfiles.SingleAsync(
                profile => profile.Id == userId,
                token);
            var profiles = await dbContext.TrainerProfiles.IgnoreQueryFilters()
                .Where(profile => profile.UserId == userId)
                .ToListAsync(token);
            if (profiles.Count > 1)
            {
                throw LifecycleConflict("The user has multiple Trainer profiles.");
            }

            var trainer = profiles.SingleOrDefault();
            var assignments = await ActiveAssignmentsAsync(userId, token);
            if (assignments.Count > 1 || assignments.Any(assignment =>
                    assignment.Role != RoleNames.Trainer ||
                    (trainer is not null && assignment.TenantId != trainer.TenantId)))
            {
                throw LifecycleConflict(
                    "The Trainer account has a conflicting active gym assignment.");
            }

            var tenantIds = assignments.Select(assignment => assignment.TenantId).ToList();
            if (trainer is not null)
            {
                tenantIds.Add(trainer.TenantId);
            }

            var distinctTenantIds = tenantIds.Distinct().ToArray();
            using var tenantWrite = distinctTenantIds.Length == 0
                ? null
                : tenantMutationScope.Begin(distinctTenantIds);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (trainer is not null)
            {
                trainer.IsActive = false;
            }

            EndAssignments(assignments, normalizedReason, now);
            EnsureRoleChangeSucceeded(await accounts.ReplaceRoleAsync(
                userId,
                RoleNames.Member,
                token));
            if (action == TrainerAdministrationAction.AccountDeactivation)
            {
                user.IsActive = false;
            }

            await RevokeSessionsAsync(
                user,
                action == TrainerAdministrationAction.AccountDeactivation
                    ? "account_deactivated"
                    : "role_revoked",
                now,
                token);
            AddLifecycleAudit(
                actorId,
                userId,
                trainer?.TenantId ?? assignments.SingleOrDefault()?.TenantId,
                trainer?.Id,
                "trainer.deactivated",
                normalizedReason,
                now);
            AddAdministrationAudit(
                actorId,
                userId,
                action,
                normalizedReason,
                now);
            await dbContext.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
    }

    private async Task ActivateAssignmentAsync(
        Guid userId,
        Guid tenantId,
        Guid actorId,
        string reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var trainerAssignments = await dbContext.UserGymAssignments.IgnoreQueryFilters()
            .Where(assignment =>
                assignment.UserId == userId &&
                assignment.TenantId == tenantId &&
                assignment.Role == RoleNames.Trainer)
            .ToListAsync(cancellationToken);
        if (trainerAssignments.Count > 1)
        {
            throw LifecycleConflict("The user has duplicate Trainer assignments.");
        }

        var assignment = trainerAssignments.SingleOrDefault();
        if (assignment is null)
        {
            dbContext.UserGymAssignments.Add(new UserGymAssignment
            {
                TenantId = tenantId,
                UserId = userId,
                Role = RoleNames.Trainer,
                Status = AssignmentStatus.Active,
                StartsAtUtc = now,
                ApprovedByUserId = actorId,
                Reason = reason,
            });
            return;
        }

        assignment.Status = AssignmentStatus.Active;
        assignment.StartsAtUtc = now;
        assignment.EndsAtUtc = null;
        assignment.ApprovedByUserId = actorId;
        assignment.Reason = reason;
    }

    private async Task<List<UserGymAssignment>> ActiveAssignmentsAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await dbContext.UserGymAssignments.IgnoreQueryFilters()
            .Where(assignment =>
                assignment.UserId == userId &&
                assignment.Status == AssignmentStatus.Active)
            .ToListAsync(cancellationToken);

    private static void EnsureDeactivationIsCompatible(
        IdentityAccount account,
        TrainerProfile trainer,
        IReadOnlyCollection<UserGymAssignment> assignments)
    {
        if (account.Role is not RoleNames.Trainer and not RoleNames.Member ||
            assignments.Any(assignment =>
                assignment.TenantId != trainer.TenantId ||
                assignment.Role is not RoleNames.Trainer and not RoleNames.Member))
        {
            throw LifecycleConflict(
                "The Trainer lifecycle is inconsistent with another active role or assignment.");
        }
    }

    private static bool IsCanonicalActive(
        IdentityAccount account,
        TrainerProfile trainer,
        List<UserGymAssignment> assignments) =>
        account.Role == RoleNames.Trainer &&
        assignments.Count(assignment =>
            assignment.Role == RoleNames.Trainer &&
            assignment.TenantId == trainer.TenantId) == 1 &&
        assignments.Count == 1;

    private static void EndAssignments(
        IEnumerable<UserGymAssignment> assignments,
        string reason,
        DateTime now)
    {
        foreach (var assignment in assignments)
        {
            assignment.Status = AssignmentStatus.Ended;
            assignment.EndsAtUtc = now;
            assignment.Reason = reason;
        }
    }

    private async Task RevokeSessionsAsync(
        UserProfile user,
        string reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var sessions = await dbContext.RefreshTokenSessions
            .Where(session => session.UserId == user.Id && session.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.RevokedAtUtc = now;
            session.RevocationReason = reason;
        }

        user.TokenVersion++;
    }

    private async Task<TrainerLifecycleResult> BuildResultAsync(
        TrainerProfile trainer,
        UserProfile user,
        CancellationToken cancellationToken)
    {
        var trainingTypeIds = await dbContext.TrainerTrainingTypes.IgnoreQueryFilters()
            .Where(item => item.TrainerProfileId == trainer.Id)
            .OrderBy(item => item.TrainingTypeId)
            .Select(item => item.TrainingTypeId)
            .ToListAsync(cancellationToken);
        return new TrainerLifecycleResult(trainer, user, trainingTypeIds);
    }

    private async Task RequireActiveTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Tenants.AsNoTracking().AnyAsync(
                tenant => tenant.Id == tenantId && tenant.Status == TenantStatus.Active,
                cancellationToken))
        {
            throw new ConflictException(
                "tenant_unavailable",
                "The tenant must be active for this Trainer lifecycle transition.");
        }
    }

    private void AddLifecycleAudit(
        Guid actorId,
        Guid userId,
        Guid? tenantId,
        Guid? trainerProfileId,
        string action,
        string reason,
        DateTime now) =>
        dbContext.SecurityAuditRecords.Add(new SecurityAuditRecord
        {
            ActorUserId = actorId,
            TargetUserId = userId,
            TargetTenantId = tenantId,
            Action = action,
            TargetType = nameof(TrainerProfile),
            TargetId = trainerProfileId,
            Reason = reason,
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAtUtc = now,
        });

    private void AddAdministrationAudit(
        Guid actorId,
        Guid userId,
        TrainerAdministrationAction action,
        string reason,
        DateTime now) =>
        dbContext.SecurityAuditRecords.Add(new SecurityAuditRecord
        {
            ActorUserId = actorId,
            TargetUserId = userId,
            Action = action switch
            {
                TrainerAdministrationAction.RoleAssignmentToMember => "user.role_assigned",
                TrainerAdministrationAction.RoleRevocation => "user.role_revoked",
                TrainerAdministrationAction.AccountDeactivation => "user.deactivated",
                _ => throw new ArgumentOutOfRangeException(nameof(action)),
            },
            TargetType = nameof(UserProfile),
            TargetId = userId,
            Reason = reason,
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAtUtc = now,
        });

    private static string NormalizeReason(string reason)
    {
        var normalized = reason.Trim();
        if (normalized.Length is < 2 or > 200)
        {
            throw new ApplicationRuleException(
                "reason_invalid",
                "Reason must contain between 2 and 200 characters.");
        }

        return normalized;
    }

    private static ConflictException LifecycleConflict(string message) =>
        new("trainer_lifecycle_conflict", message);

    private static void EnsureRoleChangeSucceeded(IdentityOperationResult result)
    {
        if (!result.Succeeded)
        {
            throw new ConflictException(
                "trainer_lifecycle_failed",
                string.Join(" ", result.Errors));
        }
    }
}
