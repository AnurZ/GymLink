using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Application.Recommendations;
using GymLink.Domain.Enums;
using GymLink.Domain.Common;
using GymLink.Domain.Trainers;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Catalog;

public sealed class TrainerOfferingService(
    IApplicationDbContext dbContext,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IIdentityAccountManager accounts,
    IRecommendationActivityRecorder recommendationActivity) : ITrainerOfferingService
{
    public async Task<PagedResult<TrainerOfferingDto>> SearchAsync(
        CatalogSearchRequest request,
        CancellationToken cancellationToken)
    {
        RequireTenant();
        request.Validate();
        var query =
            from offering in dbContext.TrainerServiceOfferings.AsNoTracking()
            join trainingType in dbContext.TrainingTypes.AsNoTracking()
                on offering.TrainingTypeId equals trainingType.Id
            select new { Offering = offering, TrainingType = trainingType.Name };
        if (tenantContext.TenantRole == RoleNames.Trainer)
        {
            var ownTrainerId = await OwnTrainerIdAsync(cancellationToken);
            query = query.Where(x => x.Offering.TrainerProfileId == ownTrainerId);
        }
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var pattern = $"%{request.Query.Trim()}%";
            query = query.Where(x =>
                EF.Functions.Like(x.Offering.Name, pattern) ||
                EF.Functions.Like(x.TrainingType, pattern));
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(x => x.Offering.IsActive == request.IsActive.Value);
        }

        return await query.OrderBy(x => x.Offering.Name).ThenBy(x => x.Offering.Id)
            .Select(x => new TrainerOfferingDto(
                x.Offering.Id,
                x.Offering.TrainerProfileId,
                x.Offering.TrainingTypeId,
                x.TrainingType,
                x.Offering.Name,
                x.Offering.DurationMinutes,
                x.Offering.Price,
                x.Offering.Currency,
                x.Offering.IsActive))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<PagedResult<TrainerOfferingDto>> GetPublicByTrainerAsync(
        Guid trainerId,
        PagedRequest request,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var trainer = await dbContext.CanonicalActiveTrainers()
            .SingleOrDefaultAsync(x => x.Id == trainerId, cancellationToken)
            ?? throw new NotFoundException("trainer_not_found", "The trainer was not found.");
        var visibleGym = await dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.TenantId == trainer.TenantId && x.IsPubliclyVisible, cancellationToken);
        if (!visibleGym || !await accounts.IsInRoleAsync(trainer.UserId, RoleNames.Trainer))
        {
            throw new NotFoundException("trainer_not_found", "The trainer was not found.");
        }

        var query =
                from offering in dbContext.TrainerServiceOfferings.IgnoreQueryFilters().AsNoTracking()
                join trainingType in dbContext.TrainingTypes.AsNoTracking()
                    on offering.TrainingTypeId equals trainingType.Id
                where offering.TrainerProfileId == trainerId && offering.IsActive
                select new { Offering = offering, TrainingTypeName = trainingType.Name };
        var totalCount = await query.LongCountAsync(cancellationToken);
        var results = await query
            .OrderBy(x => x.Offering.Name)
            .ThenBy(x => x.Offering.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new TrainerOfferingDto(
                x.Offering.Id,
                x.Offering.TrainerProfileId,
                x.Offering.TrainingTypeId,
                x.TrainingTypeName,
                x.Offering.Name,
                x.Offering.DurationMinutes,
                x.Offering.Price,
                x.Offering.Currency,
                x.Offering.IsActive))
            .ToListAsync(cancellationToken);
        await recommendationActivity.RecordReadAsync(
            ActivityEventType.TrainerView,
            trainer.TenantId,
            RecommendationTargetType.Trainer,
            trainer.Id,
            cancellationToken);
        return new PagedResult<TrainerOfferingDto>(
            results,
            request.Page,
            request.PageSize,
            totalCount);
    }

    public async Task<TrainerOfferingDto> CreateAsync(
        CreateTrainerOfferingRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var trainerId = await ResolveWritableTrainerIdAsync(
            request.TrainerProfileId,
            cancellationToken);
        await ValidateParentsAsync(
            trainerId,
            request.TrainingTypeId,
            cancellationToken);
        var name = request.Name.Trim();
        await EnsureUniqueAsync(trainerId, name, null, cancellationToken);
        var entity = new TrainerServiceOffering(
            tenantId,
            trainerId,
            request.TrainingTypeId,
            name,
            request.DurationMinutes,
            request.Price,
            request.Currency.Trim().ToUpperInvariant());
        dbContext.TrainerServiceOfferings.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        var typeName = await TrainingTypeNameAsync(entity.TrainingTypeId, cancellationToken);
        return ToDto(entity, typeName);
    }

    public async Task<TrainerOfferingDto> UpdateAsync(
        Guid id,
        UpdateTrainerOfferingRequest request,
        CancellationToken cancellationToken)
    {
        RequireTenant();
        var entity = await dbContext.TrainerServiceOfferings.SingleOrDefaultAsync(
                x => x.Id == id,
                cancellationToken)
            ?? throw new NotFoundException("trainer_offering_not_found", "The trainer offering was not found.");
        await EnsureOfferingOwnershipAsync(entity, cancellationToken);
        if (entity.TrainerProfileId != request.TrainerProfileId)
        {
            throw new ConflictException(
                "offering_trainer_immutable",
                "An offering cannot be moved to another trainer.");
        }

        await ValidateParentsAsync(
            request.TrainerProfileId,
            request.TrainingTypeId,
            cancellationToken);
        var name = request.Name.Trim();
        await EnsureUniqueAsync(request.TrainerProfileId, name, id, cancellationToken);
        entity.UpdateDetails(
            request.TrainingTypeId,
            name,
            request.DurationMinutes,
            request.Price,
            request.Currency.Trim().ToUpperInvariant(),
            request.IsActive);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(entity, await TrainingTypeNameAsync(entity.TrainingTypeId, cancellationToken));
    }

    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireTenant();
        var entity = await dbContext.TrainerServiceOfferings.SingleOrDefaultAsync(
                x => x.Id == id,
                cancellationToken)
            ?? throw new NotFoundException("trainer_offering_not_found", "The trainer offering was not found.");
        await EnsureOfferingOwnershipAsync(entity, cancellationToken);
        entity.IsActive = false;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ValidateParentsAsync(
        Guid trainerId,
        Guid trainingTypeId,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.TrainerProfiles.AnyAsync(
                x => x.Id == trainerId && x.IsActive,
                cancellationToken))
        {
            throw new NotFoundException("trainer_not_found", "The trainer was not found.");
        }

        if (!await dbContext.TrainingTypes.AnyAsync(
                x => x.Id == trainingTypeId && x.IsActive,
                cancellationToken))
        {
            throw new NotFoundException("training_type_not_found", "The training type was not found.");
        }
    }

    private async Task EnsureUniqueAsync(
        Guid trainerId,
        string name,
        Guid? excludedId,
        CancellationToken cancellationToken)
    {
        if (await dbContext.TrainerServiceOfferings.AnyAsync(
                x => x.TrainerProfileId == trainerId &&
                    x.Name == name &&
                    (!excludedId.HasValue || x.Id != excludedId.Value),
                cancellationToken))
        {
            throw new ConflictException(
                "trainer_offering_duplicate",
                "The trainer already has an offering with the same name.");
        }
    }

    private Task<string> TrainingTypeNameAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.TrainingTypes.Where(x => x.Id == id)
            .Select(x => x.Name)
            .SingleAsync(cancellationToken);

    private Guid RequireTenant() =>
        tenantContext.TenantId
        ?? throw new InvalidOperationException("A selected tenant is required.");

    private async Task<Guid> ResolveWritableTrainerIdAsync(
        Guid requestedTrainerId,
        CancellationToken cancellationToken)
    {
        if (tenantContext.TenantRole != RoleNames.Trainer)
        {
            return requestedTrainerId;
        }

        var ownTrainerId = await OwnTrainerIdAsync(cancellationToken);
        if (requestedTrainerId != Guid.Empty && requestedTrainerId != ownTrainerId)
        {
            throw new AuthorizationDeniedException(
                "trainer_ownership_required",
                "Trainers may manage only their own offerings.");
        }

        return ownTrainerId;
    }

    private async Task EnsureOfferingOwnershipAsync(
        TrainerServiceOffering offering,
        CancellationToken cancellationToken)
    {
        if (tenantContext.TenantRole == RoleNames.Trainer &&
            offering.TrainerProfileId != await OwnTrainerIdAsync(cancellationToken))
        {
            throw new NotFoundException(
                "trainer_offering_not_found",
                "The trainer offering was not found.");
        }
    }

    private async Task<Guid> OwnTrainerIdAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ??
            throw new AuthorizationDeniedException(
                "current_user_required",
                "A current user is required.");
        return await dbContext.TrainerProfiles.AsNoTracking()
                .Where(x => x.UserId == userId && x.IsActive)
                .Select(x => x.Id)
                .SingleOrDefaultAsync(cancellationToken) is var trainerId &&
            trainerId != Guid.Empty
                ? trainerId
                : throw new AuthorizationDeniedException(
                    "trainer_profile_required",
                    "An active trainer profile is required.");
    }

    private static TrainerOfferingDto ToDto(
        TrainerServiceOffering offering,
        string trainingType) =>
        new(
            offering.Id,
            offering.TrainerProfileId,
            offering.TrainingTypeId,
            trainingType,
            offering.Name,
            offering.DurationMinutes,
            offering.Price,
            offering.Currency,
            offering.IsActive);
}
