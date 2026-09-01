using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Application.TrainerImages;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Trainers;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Catalog;

internal sealed class TrainerCatalogService(
    IApplicationDbContext dbContext,
    ITenantContext tenantContext,
    IIdentityAccountManager accounts,
    ICurrentUser currentUser,
    ITrainerLifecycleCoordinator lifecycle,
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
                x.Trainer.ImageUrl,
                x.Trainer.ImageContentType,
                x.Trainer.ImageFileSizeBytes,
                x.Trainer.RowVersion,
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
                specializations.GetValueOrDefault(x.Id, []),
                x.ImageUrl,
                new TrainerImageDto(
                    x.Id,
                    x.ImageUrl,
                    x.ImageContentType,
                    x.ImageFileSizeBytes,
                    Convert.ToBase64String(x.RowVersion))))
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
                  membership.EndsAtUtc.HasValue &&
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
                EndsAtUtc = membership.EndsAtUtc.GetValueOrDefault(),
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
        var candidateAccounts = await accounts.FindByIdsAsync(
            rows.Select(row => row.MemberUserId).ToArray(),
            cancellationToken);
        var items = new List<TrainerCandidateDto>(rows.Count);
        foreach (var row in rows)
        {
            if (candidateAccounts.TryGetValue(row.MemberUserId, out var account) &&
                account.Role == RoleNames.Member)
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

    public async Task<PagedResult<TrainerDto>> GetPublicByGymAsync(
        Guid gymId,
        PagedRequest request,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var gym = await (
                from candidate in dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                join tenant in dbContext.Tenants.AsNoTracking() on candidate.TenantId equals tenant.Id
                where candidate.Id == gymId &&
                    candidate.IsPubliclyVisible &&
                    tenant.Status == TenantStatus.Active
                select new { candidate.TenantId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("gym_not_found", "The gym was not found.");
        var trainerUserIds = await accounts.GetUserIdsInRoleAsync(
            RoleNames.Trainer,
            cancellationToken);
        var query =
                from trainer in dbContext.CanonicalActiveTrainers()
                join user in dbContext.UserProfiles.AsNoTracking() on trainer.UserId equals user.Id
                where trainer.TenantId == gym.TenantId && trainerUserIds.Contains(trainer.UserId)
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
                    trainer.ImageUrl,
                };
        var totalCount = await query.LongCountAsync(cancellationToken);
        var rows = await query
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var specializations = await LoadSpecializationsAsync(
            rows.Select(x => x.Id),
            true,
            cancellationToken);
        var items = rows.Select(x => new TrainerDto(
                x.Id,
                x.UserId,
                x.DisplayName,
                x.Biography,
                x.Credentials,
                x.IsActive,
                x.AverageRating,
                x.ReviewCount,
                specializations.GetValueOrDefault(x.Id, []),
                x.ImageUrl,
                null))
            .ToList();
        return new PagedResult<TrainerDto>(items, request.Page, request.PageSize, totalCount);
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
        var result = await lifecycle.PromoteAsync(
            tenantId,
            request,
            trainingTypeIds,
            actorId,
            cancellationToken);
        return ToDto(result.Profile, result.User.DisplayName, result.TrainingTypeIds);
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

    public async Task<TrainerDto> DeactivateAsync(
        Guid id,
        TrainerLifecycleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await lifecycle.DeactivateAsync(
            RequireTenant(),
            id,
            request.Reason,
            RequireActor(),
            cancellationToken);
        return ToDto(result.Profile, result.User.DisplayName, result.TrainingTypeIds);
    }

    public async Task<TrainerDto> ReactivateAsync(
        Guid id,
        TrainerLifecycleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await lifecycle.ReactivateAsync(
            RequireTenant(),
            id,
            request.Reason,
            RequireActor(),
            cancellationToken);
        return ToDto(result.Profile, result.User.DisplayName, result.TrainingTypeIds);
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

    private Guid RequireActor() =>
        currentUser.UserId
        ?? throw new AuthenticationFailedException(
            "authentication_required",
            "Authentication is required.");

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
            trainingTypeIds,
            trainer.ImageUrl,
            new TrainerImageDto(
                trainer.Id,
                trainer.ImageUrl,
                trainer.ImageContentType,
                trainer.ImageFileSizeBytes,
                Convert.ToBase64String(trainer.RowVersion)));

}
