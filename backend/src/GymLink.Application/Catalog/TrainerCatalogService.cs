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

public sealed class TrainerCatalogService(
    IApplicationDbContext dbContext,
    ITenantContext tenantContext,
    IIdentityAccountManager accounts,
    IApplicationTransaction transaction,
    ICurrentUser currentUser,
    ITenantMutationScope tenantMutationScope,
    TimeProvider timeProvider) : ITrainerCatalogService
{
    public async Task<PagedResult<TrainerDto>> SearchAsync(
        TrainerSearchRequest request,
        CancellationToken cancellationToken)
    {
        RequireTenant();
        request.Validate();
        var query =
            from trainer in dbContext.TrainerProfiles.AsNoTracking()
            join user in dbContext.UserProfiles.AsNoTracking() on trainer.UserId equals user.Id
            select new { Trainer = trainer, User = user };

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var pattern = $"%{request.Query.Trim()}%";
            query = query.Where(x =>
                EF.Functions.Like(x.User.DisplayName, pattern) ||
                EF.Functions.Like(x.Trainer.Biography, pattern));
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(x => x.Trainer.IsActive == request.IsActive.Value);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderBy(x => x.User.DisplayName).ThenBy(x => x.Trainer.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new
            {
                x.Trainer.Id,
                x.Trainer.UserId,
                x.User.DisplayName,
                x.Trainer.Biography,
                x.Trainer.Credentials,
                x.Trainer.IsActive,
                x.Trainer.AverageRating,
                x.Trainer.ReviewCount,
            })
            .ToListAsync(cancellationToken);
        var specializations = await LoadSpecializationsAsync(rows.Select(x => x.Id), false, cancellationToken);
        var items = rows.Select(x => new TrainerDto(
                x.Id,
                x.UserId,
                x.DisplayName,
                x.Biography,
                x.Credentials,
                x.IsActive,
                x.AverageRating,
                x.ReviewCount,
                specializations.GetValueOrDefault(x.Id, [])))
            .ToList();
        return new PagedResult<TrainerDto>(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<PagedResult<TrainerCandidateDto>> SearchCandidatesAsync(
        TrainerCandidateSearchRequest request,
        CancellationToken cancellationToken)
    {
        RequireTenant();
        request.Validate();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var query =
            from membership in dbContext.Memberships.AsNoTracking()
            join user in dbContext.UserProfiles.AsNoTracking()
                on membership.MemberUserId equals user.Id
            where membership.Status == MembershipStatus.Active &&
                  membership.EndsAtUtc > now &&
                  user.IsActive &&
                  dbContext.UserGymAssignments.Any(
                      assignment =>
                          assignment.UserId == membership.MemberUserId &&
                          assignment.Role == RoleNames.Member &&
                          assignment.Status == AssignmentStatus.Active) &&
                  !dbContext.TrainerProfiles.Any(
                      trainer => trainer.UserId == membership.MemberUserId)
            select new
            {
                membership.MemberUserId,
                user.DisplayName,
                membership.PlanName,
                membership.EndsAtUtc,
            };

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var pattern = $"%{request.Query.Trim()}%";
            query = query.Where(candidate =>
                EF.Functions.Like(candidate.DisplayName, pattern));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var rows = await query
            .OrderBy(candidate => candidate.DisplayName)
            .ThenBy(candidate => candidate.MemberUserId)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var items = new List<TrainerCandidateDto>(rows.Count);
        foreach (var row in rows)
        {
            var account = await accounts.FindByIdAsync(row.MemberUserId, cancellationToken);
            if (account?.Role == RoleNames.Member)
            {
                items.Add(new TrainerCandidateDto(
                    row.MemberUserId,
                    row.DisplayName,
                    account.Email,
                    row.PlanName,
                    row.EndsAtUtc));
            }
        }

        return new PagedResult<TrainerCandidateDto>(
            items,
            request.Page,
            request.PageSize,
            totalCount);
    }

    public async Task<IReadOnlyList<TrainerDto>> GetPublicByGymAsync(
        Guid gymId,
        CancellationToken cancellationToken)
    {
        var gym = await (
                from candidate in dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                join tenant in dbContext.Tenants.AsNoTracking() on candidate.TenantId equals tenant.Id
                where candidate.Id == gymId &&
                    candidate.IsPubliclyVisible &&
                    tenant.Status == TenantStatus.Active
                select new { candidate.TenantId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("gym_not_found", "The gym was not found.");
        var rows = await (
                from trainer in dbContext.TrainerProfiles.IgnoreQueryFilters().AsNoTracking()
                join user in dbContext.UserProfiles.AsNoTracking() on trainer.UserId equals user.Id
                where trainer.TenantId == gym.TenantId && trainer.IsActive && user.IsActive
                orderby user.DisplayName
                select new
                {
                    trainer.Id,
                    trainer.UserId,
                    user.DisplayName,
                    trainer.Biography,
                    trainer.Credentials,
                    trainer.IsActive,
                    trainer.AverageRating,
                    trainer.ReviewCount,
                })
            .ToListAsync(cancellationToken);
        var specializations = await LoadSpecializationsAsync(
            rows.Select(x => x.Id),
            true,
            cancellationToken);
        return rows.Select(x => new TrainerDto(
                x.Id,
                x.UserId,
                x.DisplayName,
                x.Biography,
                x.Credentials,
                x.IsActive,
                x.AverageRating,
                x.ReviewCount,
                specializations.GetValueOrDefault(x.Id, [])))
            .ToList();
    }

    public async Task<TrainerDto> CreateAsync(
        CreateTrainerRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var actorId = currentUser.UserId
            ?? throw new AuthenticationFailedException(
                "authentication_required",
                "Authentication is required.");
        var trainingTypeIds = await ValidateTrainingTypesAsync(
            request.TrainingTypeIds,
            cancellationToken);
        return await transaction.ExecuteSerializableAsync(async token =>
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
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

            var assignments = await dbContext.UserGymAssignments
                .IgnoreQueryFilters()
                .Where(assignment =>
                    assignment.UserId == request.UserId &&
                    assignment.Status == AssignmentStatus.Active)
                .ToListAsync(token);
            if (assignments.Any(assignment =>
                    assignment.TenantId != tenantId ||
                    assignment.Role != RoleNames.Member))
            {
                throw new ConflictException(
                    "trainer_assignment_conflict",
                    "The selected member already has a conflicting active gym assignment.");
            }

            using var tenantWrite = tenantMutationScope.Begin(tenantId);
            foreach (var assignment in assignments)
            {
                assignment.Status = AssignmentStatus.Ended;
                assignment.EndsAtUtc = now;
                assignment.Reason = request.Reason.Trim();
            }

            var trainerAssignment = await dbContext.UserGymAssignments
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    assignment =>
                        assignment.UserId == request.UserId &&
                        assignment.TenantId == tenantId &&
                        assignment.Role == RoleNames.Trainer,
                    token);
            if (trainerAssignment is null)
            {
                dbContext.UserGymAssignments.Add(new UserGymAssignment
                {
                    TenantId = tenantId,
                    UserId = request.UserId,
                    Role = RoleNames.Trainer,
                    Status = AssignmentStatus.Active,
                    StartsAtUtc = now,
                    ApprovedByUserId = actorId,
                    Reason = request.Reason.Trim(),
                });
            }
            else
            {
                trainerAssignment.Status = AssignmentStatus.Active;
                trainerAssignment.StartsAtUtc = now;
                trainerAssignment.EndsAtUtc = null;
                trainerAssignment.ApprovedByUserId = actorId;
                trainerAssignment.Reason = request.Reason.Trim();
            }

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

            EnsureSucceeded(await accounts.ReplaceRoleAsync(
                request.UserId,
                RoleNames.Trainer,
                token));
            var sessions = await dbContext.RefreshTokenSessions
                .Where(session =>
                    session.UserId == request.UserId &&
                    session.RevokedAtUtc == null)
                .ToListAsync(token);
            foreach (var session in sessions)
            {
                session.RevokedAtUtc = now;
                session.RevocationReason = "role_changed";
            }

            user.TokenVersion++;
            dbContext.SecurityAuditRecords.Add(new SecurityAuditRecord
            {
                ActorUserId = actorId,
                TargetUserId = request.UserId,
                TargetTenantId = tenantId,
                Action = "trainer.promoted",
                TargetType = nameof(TrainerProfile),
                TargetId = trainer.Id,
                Reason = request.Reason.Trim(),
                CorrelationId = Guid.NewGuid().ToString("N"),
                OccurredAtUtc = now,
            });
            await dbContext.SaveChangesAsync(token);
            return ToDto(trainer, user.DisplayName, trainingTypeIds);
        }, cancellationToken);
    }

    public async Task<TrainerDto> UpdateAsync(
        Guid id,
        UpdateTrainerRequest request,
        CancellationToken cancellationToken)
    {
        RequireTenant();
        var trainer = await dbContext.TrainerProfiles.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("trainer_not_found", "The trainer was not found.");
        if (trainer.UserId != request.UserId)
        {
            throw new ConflictException("trainer_user_immutable", "A trainer profile cannot be moved to another user.");
        }

        var user = await dbContext.UserProfiles.AsNoTracking()
            .SingleAsync(x => x.Id == trainer.UserId, cancellationToken);
        var trainingTypeIds = await ValidateTrainingTypesAsync(request.TrainingTypeIds, cancellationToken);
        trainer.Biography = request.Biography.Trim();
        trainer.Credentials = request.Credentials?.Trim();
        trainer.IsActive = request.IsActive;
        dbContext.TrainerTrainingTypes.RemoveRange(
            await dbContext.TrainerTrainingTypes.Where(x => x.TrainerProfileId == id)
                .ToListAsync(cancellationToken));
        dbContext.TrainerTrainingTypes.AddRange(trainingTypeIds.Select(trainingTypeId =>
            new TrainerTrainingType
            {
                TrainerProfileId = id,
                TrainingTypeId = trainingTypeId,
            }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(trainer, user.DisplayName, trainingTypeIds);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireTenant();
        var trainer = await dbContext.TrainerProfiles.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("trainer_not_found", "The trainer was not found.");
        trainer.IsActive = false;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, IReadOnlyList<Guid>>> LoadSpecializationsAsync(
        IEnumerable<Guid> trainerIds,
        bool ignoreTenantFilter,
        CancellationToken cancellationToken)
    {
        var ids = trainerIds.ToArray();
        var query = dbContext.TrainerTrainingTypes.AsNoTracking();
        if (ignoreTenantFilter)
        {
            query = query.IgnoreQueryFilters();
        }

        var rows = await query.Where(x => ids.Contains(x.TrainerProfileId))
            .OrderBy(x => x.TrainingTypeId)
            .Select(x => new { x.TrainerProfileId, x.TrainingTypeId })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(x => x.TrainerProfileId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Guid>)group.Select(x => x.TrainingTypeId).ToList());
    }

    private async Task<Guid[]> ValidateTrainingTypesAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct().ToArray();
        if (await dbContext.TrainingTypes.CountAsync(
                x => distinct.Contains(x.Id) && x.IsActive,
                cancellationToken) != distinct.Length)
        {
            throw new NotFoundException("training_type_not_found", "One or more training types were not found.");
        }

        return distinct;
    }

    private Guid RequireTenant()
    {
        return tenantContext.TenantId
            ?? throw new InvalidOperationException("A selected tenant is required.");
    }

    private static TrainerDto ToDto(
        TrainerProfile trainer,
        string displayName,
        IReadOnlyList<Guid> trainingTypeIds) =>
        new(
            trainer.Id,
            trainer.UserId,
            displayName,
            trainer.Biography,
            trainer.Credentials,
            trainer.IsActive,
            trainer.AverageRating,
            trainer.ReviewCount,
            trainingTypeIds);

    private static void EnsureSucceeded(IdentityOperationResult result)
    {
        if (!result.Succeeded)
        {
            throw new ConflictException(
                "trainer_promotion_failed",
                string.Join(" ", result.Errors));
        }
    }
}
