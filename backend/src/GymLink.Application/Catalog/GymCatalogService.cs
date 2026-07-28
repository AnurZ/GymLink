using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Domain.Catalog;
using GymLink.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Catalog;

public sealed class GymCatalogService(
    IApplicationDbContext dbContext,
    ITenantContext tenantContext) : IGymCatalogService
{
    public Task<PagedResult<GymListItemDto>> SearchPublicAsync(
        GymSearchRequest request,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var query =
            from gym in dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
            join tenant in dbContext.Tenants.AsNoTracking() on gym.TenantId equals tenant.Id
            join city in dbContext.Cities.AsNoTracking() on gym.CityId equals city.Id
            where gym.IsPubliclyVisible && tenant.Status == TenantStatus.Active
            select new { Gym = gym, City = city };

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var pattern = $"%{request.Query.Trim()}%";
            query = query.Where(x =>
                EF.Functions.Like(x.Gym.Name, pattern) ||
                EF.Functions.Like(x.Gym.Address, pattern) ||
                EF.Functions.Like(x.City.Name, pattern));
        }

        if (request.CityId.HasValue)
        {
            query = query.Where(x => x.Gym.CityId == request.CityId.Value);
        }

        return query.OrderBy(x => x.Gym.Name).ThenBy(x => x.Gym.Id)
            .Select(x => new GymListItemDto(
                x.Gym.Id,
                x.Gym.Name,
                x.Gym.Address,
                x.City.Name,
                x.Gym.Latitude,
                x.Gym.Longitude,
                dbContext.GymImages.IgnoreQueryFilters()
                    .Where(image => image.GymId == x.Gym.Id && image.IsPrimary)
                    .Select(image => image.PublicUrl)
                    .FirstOrDefault(),
                dbContext.MembershipPlans.IgnoreQueryFilters()
                    .Where(plan => plan.GymId == x.Gym.Id && plan.IsActive)
                    .Select(plan => (decimal?)plan.Price)
                    .Min(),
                dbContext.MembershipPlans.IgnoreQueryFilters()
                    .Where(plan => plan.GymId == x.Gym.Id && plan.IsActive)
                    .OrderBy(plan => plan.Price)
                    .Select(plan => plan.Currency)
                    .FirstOrDefault(),
                x.Gym.AverageRating,
                x.Gym.ReviewCount))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<GymDetailsDto> GetPublicDetailsAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var visible = await (
                from gym in dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                join tenant in dbContext.Tenants.AsNoTracking() on gym.TenantId equals tenant.Id
                where gym.Id == id &&
                    gym.IsPubliclyVisible &&
                    tenant.Status == TenantStatus.Active
                select gym)
            .AnyAsync(cancellationToken);
        if (!visible)
        {
            throw new NotFoundException("gym_not_found", "The gym was not found.");
        }

        return await BuildDetailsAsync(id, true, cancellationToken);
    }

    public async Task<GymDetailsDto> GetCurrentTenantGymAsync(CancellationToken cancellationToken)
    {
        RequireTenant();
        var id = await dbContext.Gyms.AsNoTracking()
            .Select(x => x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            throw new NotFoundException("gym_not_found", "No gym exists for the current tenant.");
        }

        return await BuildDetailsAsync(id, false, cancellationToken);
    }

    public async Task<GymDetailsDto> UpdateCurrentTenantGymAsync(
        UpdateGymRequest request,
        CancellationToken cancellationToken)
    {
        RequireTenant();
        ValidateWorkingHours(request.WorkingHours);
        var gym = await dbContext.Gyms.SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("gym_not_found", "No gym exists for the current tenant.");
        if (!await dbContext.Cities.AnyAsync(
                x => x.Id == request.CityId && x.IsActive,
                cancellationToken))
        {
            throw new NotFoundException("city_not_found", "The selected city was not found.");
        }

        var equipmentIds = request.EquipmentIds.Distinct().ToArray();
        var trainingTypeIds = request.TrainingTypeIds.Distinct().ToArray();
        if (await dbContext.Equipment.CountAsync(
                x => equipmentIds.Contains(x.Id) && x.IsActive,
                cancellationToken) != equipmentIds.Length)
        {
            throw new NotFoundException("equipment_not_found", "One or more equipment values were not found.");
        }

        if (await dbContext.TrainingTypes.CountAsync(
                x => trainingTypeIds.Contains(x.Id) && x.IsActive,
                cancellationToken) != trainingTypeIds.Length)
        {
            throw new NotFoundException("training_type_not_found", "One or more training types were not found.");
        }

        gym.Name = request.Name.Trim();
        gym.Description = request.Description.Trim();
        gym.Address = request.Address.Trim();
        gym.CityId = request.CityId;
        gym.Latitude = request.Latitude;
        gym.Longitude = request.Longitude;
        gym.PhoneNumber = request.PhoneNumber?.Trim();

        dbContext.GymEquipment.RemoveRange(
            await dbContext.GymEquipment.Where(x => x.GymId == gym.Id).ToListAsync(cancellationToken));
        dbContext.GymTrainingTypes.RemoveRange(
            await dbContext.GymTrainingTypes.Where(x => x.GymId == gym.Id).ToListAsync(cancellationToken));
        dbContext.GymWorkingHours.RemoveRange(
            await dbContext.GymWorkingHours.Where(x => x.GymId == gym.Id).ToListAsync(cancellationToken));

        dbContext.GymEquipment.AddRange(equipmentIds.Select(id => new GymEquipment
        {
            GymId = gym.Id,
            EquipmentId = id,
        }));
        dbContext.GymTrainingTypes.AddRange(trainingTypeIds.Select(id => new GymTrainingType
        {
            GymId = gym.Id,
            TrainingTypeId = id,
        }));
        dbContext.GymWorkingHours.AddRange(request.WorkingHours.Select(hours => new GymWorkingHours
        {
            GymId = gym.Id,
            DayOfWeek = (DayOfWeek)hours.DayOfWeek,
            OpensAt = hours.OpensAt,
            ClosesAt = hours.ClosesAt,
            IsClosed = hours.IsClosed,
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await BuildDetailsAsync(gym.Id, false, cancellationToken);
    }

    private async Task<GymDetailsDto> BuildDetailsAsync(
        Guid id,
        bool ignoreTenantFilter,
        CancellationToken cancellationToken)
    {
        var gyms = dbContext.Gyms.AsNoTracking();
        var images = dbContext.GymImages.AsNoTracking();
        var gymEquipment = dbContext.GymEquipment.AsNoTracking();
        var gymTrainingTypes = dbContext.GymTrainingTypes.AsNoTracking();
        var workingHours = dbContext.GymWorkingHours.AsNoTracking();
        if (ignoreTenantFilter)
        {
            gyms = gyms.IgnoreQueryFilters();
            images = images.IgnoreQueryFilters();
            gymEquipment = gymEquipment.IgnoreQueryFilters();
            gymTrainingTypes = gymTrainingTypes.IgnoreQueryFilters();
            workingHours = workingHours.IgnoreQueryFilters();
        }

        var core = await (
                from gym in gyms
                join city in dbContext.Cities.AsNoTracking() on gym.CityId equals city.Id
                where gym.Id == id
                select new
                {
                    Gym = gym,
                    CityName = city.Name,
                })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("gym_not_found", "The gym was not found.");
        var equipment = await (
                from link in gymEquipment
                join item in dbContext.Equipment.AsNoTracking() on link.EquipmentId equals item.Id
                where link.GymId == id
                orderby item.Name
                select new { item.Id, item.Name })
            .ToListAsync(cancellationToken);
        var trainingTypes = await (
                from link in gymTrainingTypes
                join type in dbContext.TrainingTypes.AsNoTracking() on link.TrainingTypeId equals type.Id
                where link.GymId == id
                orderby type.Name
                select new { type.Id, type.Name })
            .ToListAsync(cancellationToken);
        var hourEntities = await workingHours.Where(x => x.GymId == id)
            .ToListAsync(cancellationToken);
        var hours = hourEntities
            .OrderBy(x => (int)x.DayOfWeek)
            .Select(x => new WorkingHoursDto(
                (int)x.DayOfWeek,
                x.OpensAt,
                x.ClosesAt,
                x.IsClosed))
            .ToArray();
        var imageUrls = await images.Where(x => x.GymId == id && x.PublicUrl != null)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.PublicUrl!)
            .ToListAsync(cancellationToken);

        return new GymDetailsDto(
            core.Gym.Id,
            core.Gym.Name,
            core.Gym.Description,
            core.Gym.Address,
            core.Gym.CityId,
            core.CityName,
            core.Gym.Latitude,
            core.Gym.Longitude,
            core.Gym.PhoneNumber,
            core.Gym.AverageRating,
            core.Gym.ReviewCount,
            equipment.Select(x => x.Id).ToArray(),
            equipment.Select(x => x.Name).ToArray(),
            trainingTypes.Select(x => x.Id).ToArray(),
            trainingTypes.Select(x => x.Name).ToArray(),
            hours,
            imageUrls);
    }

    private void RequireTenant()
    {
        if (!tenantContext.HasTenant)
        {
            throw new InvalidOperationException("A selected tenant is required.");
        }
    }

    private static void ValidateWorkingHours(IReadOnlyList<WorkingHoursRequest> workingHours)
    {
        if (workingHours.Select(x => x.DayOfWeek).Distinct().Count() != workingHours.Count)
        {
            throw new ConflictException("working_hours_duplicate_day", "Working hours contain a duplicate day.");
        }

        if (workingHours.Any(x =>
                !x.IsClosed &&
                (!x.OpensAt.HasValue || !x.ClosesAt.HasValue || x.ClosesAt <= x.OpensAt)))
        {
            throw new ConflictException(
                "working_hours_invalid",
                "Open days require an opening time before the closing time.");
        }
    }
}
