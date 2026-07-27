using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Trainers;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Catalog;

public sealed class TrainerCatalogService(
    IApplicationDbContext dbContext,
    ITenantContext tenantContext) : ITrainerCatalogService
{
    public async Task<PagedResult<TrainerDto>> SearchAsync(
        TrainerSearchRequest request,
        CancellationToken cancellationToken)
    {
        RequireTenant();
        request.Validate();
        var query =
            from trainer in dbContext.TrainerProfiles.AsNoTracking()
            join user in dbContext.Users.AsNoTracking() on trainer.UserId equals user.Id
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
                join user in dbContext.Users.AsNoTracking() on trainer.UserId equals user.Id
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
        var user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.UserId && x.IsActive, cancellationToken)
            ?? throw new NotFoundException("user_not_found", "The selected user was not found.");
        if (await dbContext.TrainerProfiles.AnyAsync(x => x.UserId == request.UserId, cancellationToken))
        {
            throw new ConflictException("trainer_duplicate", "This user already has a trainer profile in the gym.");
        }

        var trainingTypeIds = await ValidateTrainingTypesAsync(request.TrainingTypeIds, cancellationToken);
        var trainer = new TrainerProfile
        {
            TenantId = tenantId,
            UserId = request.UserId,
            Biography = request.Biography.Trim(),
            Credentials = request.Credentials?.Trim(),
        };
        dbContext.TrainerProfiles.Add(trainer);
        dbContext.TrainerTrainingTypes.AddRange(trainingTypeIds.Select(id => new TrainerTrainingType
        {
            TenantId = tenantId,
            TrainerProfileId = trainer.Id,
            TrainingTypeId = id,
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(trainer, user.DisplayName, trainingTypeIds);
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

        var user = await dbContext.Users.AsNoTracking()
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
}
