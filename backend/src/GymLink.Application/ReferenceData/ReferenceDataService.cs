using AutoMapper;
using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Domain.ReferenceData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GymLink.Application.ReferenceData;

public sealed class ReferenceDataService(
    IApplicationDbContext dbContext,
    IMapper mapper,
    IMemoryCache cache) : IReferenceDataService
{
    private const string LookupsCacheKey = "reference-data:active:v1";

    public Task<PagedResult<CountryDto>> SearchCountriesAsync(
        ReferenceSearchRequest request,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var query = dbContext.Countries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var pattern = $"%{request.Query.Trim()}%";
            query = query.Where(x =>
                EF.Functions.Like(x.Name, pattern) ||
                EF.Functions.Like(x.Code, pattern));
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(x => x.IsActive == request.IsActive.Value);
        }

        return query.OrderBy(x => x.Name)
            .Select(x => new CountryDto(x.Id, x.Code, x.Name, x.IsActive))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<CountryDto> CreateCountryAsync(
        CreateCountryRequest request,
        CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();
        if (await dbContext.Countries.AnyAsync(
                x => x.Code == code || x.Name == name,
                cancellationToken))
        {
            throw new ConflictException("country_duplicate", "A country with the same code or name already exists.");
        }

        var entity = new Country { Code = code, Name = name };
        dbContext.Countries.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        InvalidateLookups();
        return mapper.Map<CountryDto>(entity);
    }

    public async Task<CountryDto> UpdateCountryAsync(
        Guid id,
        UpdateCountryRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await FindRequiredAsync(dbContext.Countries, id, "country_not_found", cancellationToken);
        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();
        if (await dbContext.Countries.AnyAsync(
                x => x.Id != id && (x.Code == code || x.Name == name),
                cancellationToken))
        {
            throw new ConflictException("country_duplicate", "A country with the same code or name already exists.");
        }

        entity.Code = code;
        entity.Name = name;
        entity.IsActive = request.IsActive;
        await dbContext.SaveChangesAsync(cancellationToken);
        InvalidateLookups();
        return mapper.Map<CountryDto>(entity);
    }

    public Task DeleteCountryAsync(Guid id, CancellationToken cancellationToken) =>
        DeleteAsync(dbContext.Countries, id, "country_not_found", cancellationToken);

    public Task<PagedResult<CityDto>> SearchCitiesAsync(
        CitySearchRequest request,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var query =
            from city in dbContext.Cities.AsNoTracking()
            join country in dbContext.Countries.AsNoTracking() on city.CountryId equals country.Id
            select new { City = city, CountryName = country.Name };

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var pattern = $"%{request.Query.Trim()}%";
            query = query.Where(x =>
                EF.Functions.Like(x.City.Name, pattern) ||
                EF.Functions.Like(x.CountryName, pattern));
        }

        if (request.CountryId.HasValue)
        {
            query = query.Where(x => x.City.CountryId == request.CountryId.Value);
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(x => x.City.IsActive == request.IsActive.Value);
        }

        return query.OrderBy(x => x.CountryName).ThenBy(x => x.City.Name)
            .Select(x => new CityDto(
                x.City.Id,
                x.City.CountryId,
                x.CountryName,
                x.City.Name,
                x.City.IsActive))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<CityDto> CreateCityAsync(
        CreateCityRequest request,
        CancellationToken cancellationToken)
    {
        var country = await FindRequiredAsync(
            dbContext.Countries,
            request.CountryId,
            "country_not_found",
            cancellationToken);
        var name = request.Name.Trim();
        if (await dbContext.Cities.AnyAsync(
                x => x.CountryId == request.CountryId && x.Name == name,
                cancellationToken))
        {
            throw new ConflictException("city_duplicate", "This city already exists in the selected country.");
        }

        var entity = new City { CountryId = request.CountryId, Name = name };
        dbContext.Cities.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        InvalidateLookups();
        return new CityDto(entity.Id, entity.CountryId, country.Name, entity.Name, entity.IsActive);
    }

    public async Task<CityDto> UpdateCityAsync(
        Guid id,
        UpdateCityRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await FindRequiredAsync(dbContext.Cities, id, "city_not_found", cancellationToken);
        var country = await FindRequiredAsync(
            dbContext.Countries,
            request.CountryId,
            "country_not_found",
            cancellationToken);
        var name = request.Name.Trim();
        if (await dbContext.Cities.AnyAsync(
                x => x.Id != id && x.CountryId == request.CountryId && x.Name == name,
                cancellationToken))
        {
            throw new ConflictException("city_duplicate", "This city already exists in the selected country.");
        }

        entity.CountryId = request.CountryId;
        entity.Name = name;
        entity.IsActive = request.IsActive;
        await dbContext.SaveChangesAsync(cancellationToken);
        InvalidateLookups();
        return new CityDto(entity.Id, entity.CountryId, country.Name, entity.Name, entity.IsActive);
    }

    public Task DeleteCityAsync(Guid id, CancellationToken cancellationToken) =>
        DeleteAsync(dbContext.Cities, id, "city_not_found", cancellationToken);

    public Task<PagedResult<EquipmentDto>> SearchEquipmentAsync(
        ReferenceSearchRequest request,
        CancellationToken cancellationToken)
    {
        var query = ApplyReferenceFilter(dbContext.Equipment.AsNoTracking(), request);
        return query.OrderBy(x => x.Name)
            .Select(x => new EquipmentDto(x.Id, x.Name, x.IsActive))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<EquipmentDto> CreateEquipmentAsync(
        CreateEquipmentRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        await EnsureUniqueNameAsync(dbContext.Equipment, name, null, "equipment_duplicate", cancellationToken);
        var entity = new Equipment { Name = name };
        dbContext.Equipment.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        InvalidateLookups();
        return mapper.Map<EquipmentDto>(entity);
    }

    public async Task<EquipmentDto> UpdateEquipmentAsync(
        Guid id,
        UpdateEquipmentRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await FindRequiredAsync(dbContext.Equipment, id, "equipment_not_found", cancellationToken);
        var name = request.Name.Trim();
        await EnsureUniqueNameAsync(dbContext.Equipment, name, id, "equipment_duplicate", cancellationToken);
        entity.Name = name;
        entity.IsActive = request.IsActive;
        await dbContext.SaveChangesAsync(cancellationToken);
        InvalidateLookups();
        return mapper.Map<EquipmentDto>(entity);
    }

    public Task DeleteEquipmentAsync(Guid id, CancellationToken cancellationToken) =>
        DeleteAsync(dbContext.Equipment, id, "equipment_not_found", cancellationToken);

    public Task<PagedResult<TrainingTypeDto>> SearchTrainingTypesAsync(
        ReferenceSearchRequest request,
        CancellationToken cancellationToken)
    {
        var query = ApplyReferenceFilter(dbContext.TrainingTypes.AsNoTracking(), request);
        return query.OrderBy(x => x.Name)
            .Select(x => new TrainingTypeDto(x.Id, x.Name, x.Description, x.IsActive))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<TrainingTypeDto> CreateTrainingTypeAsync(
        CreateTrainingTypeRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        await EnsureUniqueNameAsync(dbContext.TrainingTypes, name, null, "training_type_duplicate", cancellationToken);
        var entity = new TrainingType { Name = name, Description = request.Description?.Trim() };
        dbContext.TrainingTypes.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        InvalidateLookups();
        return mapper.Map<TrainingTypeDto>(entity);
    }

    public async Task<TrainingTypeDto> UpdateTrainingTypeAsync(
        Guid id,
        UpdateTrainingTypeRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await FindRequiredAsync(dbContext.TrainingTypes, id, "training_type_not_found", cancellationToken);
        var name = request.Name.Trim();
        await EnsureUniqueNameAsync(dbContext.TrainingTypes, name, id, "training_type_duplicate", cancellationToken);
        entity.Name = name;
        entity.Description = request.Description?.Trim();
        entity.IsActive = request.IsActive;
        await dbContext.SaveChangesAsync(cancellationToken);
        InvalidateLookups();
        return mapper.Map<TrainingTypeDto>(entity);
    }

    public Task DeleteTrainingTypeAsync(Guid id, CancellationToken cancellationToken) =>
        DeleteAsync(dbContext.TrainingTypes, id, "training_type_not_found", cancellationToken);

    public async Task<ReferenceLookupsDto> GetActiveLookupsAsync(CancellationToken cancellationToken)
    {
        var result = await cache.GetOrCreateAsync(
            LookupsCacheKey,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                var countries = await dbContext.Countries.AsNoTracking()
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.Name)
                    .ThenBy(x => x.Id)
                    .Select(x => new CountryDto(x.Id, x.Code, x.Name, x.IsActive))
                    .Take(PagedRequest.MaximumPageSize)
                    .ToListAsync(cancellationToken);
                var cities = await (
                        from city in dbContext.Cities.AsNoTracking()
                        join country in dbContext.Countries.AsNoTracking()
                            on city.CountryId equals country.Id
                        where city.IsActive && country.IsActive
                        orderby country.Name, city.Name, city.Id
                        select new CityDto(city.Id, city.CountryId, country.Name, city.Name, city.IsActive))
                    .Take(PagedRequest.MaximumPageSize)
                    .ToListAsync(cancellationToken);
                var equipment = await dbContext.Equipment.AsNoTracking()
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.Name)
                    .ThenBy(x => x.Id)
                    .Select(x => new EquipmentDto(x.Id, x.Name, x.IsActive))
                    .Take(PagedRequest.MaximumPageSize)
                    .ToListAsync(cancellationToken);
                var trainingTypes = await dbContext.TrainingTypes.AsNoTracking()
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.Name)
                    .ThenBy(x => x.Id)
                    .Select(x => new TrainingTypeDto(x.Id, x.Name, x.Description, x.IsActive))
                    .Take(PagedRequest.MaximumPageSize)
                    .ToListAsync(cancellationToken);
                return new ReferenceLookupsDto(countries, cities, equipment, trainingTypes);
            });
        return result!;
    }

    private static IQueryable<T> ApplyReferenceFilter<T>(
        IQueryable<T> query,
        ReferenceSearchRequest request)
        where T : Domain.Common.Entity
    {
        request.Validate();
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var pattern = $"%{request.Query.Trim()}%";
            query = query.Where(x => EF.Functions.Like(EF.Property<string>(x, "Name"), pattern));
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(x => EF.Property<bool>(x, "IsActive") == request.IsActive.Value);
        }

        return query;
    }

    private async Task DeleteAsync<T>(
        DbSet<T> set,
        Guid id,
        string notFoundCode,
        CancellationToken cancellationToken)
        where T : Domain.Common.Entity
    {
        var entity = await FindRequiredAsync(set, id, notFoundCode, cancellationToken);
        set.Remove(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new ConflictException(
                "reference_in_use",
                "This reference value is in use and cannot be deleted.",
                exception);
        }

        InvalidateLookups();
    }

    private static async Task<T> FindRequiredAsync<T>(
        DbSet<T> set,
        Guid id,
        string code,
        CancellationToken cancellationToken)
        where T : Domain.Common.Entity
    {
        return await set.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(code, "The requested reference value was not found.");
    }

    private static async Task EnsureUniqueNameAsync<T>(
        IQueryable<T> query,
        string name,
        Guid? excludedId,
        string code,
        CancellationToken cancellationToken)
        where T : Domain.Common.Entity
    {
        var duplicate = typeof(T) == typeof(Equipment)
            ? await query.Cast<Equipment>().AnyAsync(
                x => x.Name == name && (!excludedId.HasValue || x.Id != excludedId.Value),
                cancellationToken)
            : await query.Cast<TrainingType>().AnyAsync(
                x => x.Name == name && (!excludedId.HasValue || x.Id != excludedId.Value),
                cancellationToken);
        if (duplicate)
        {
            throw new ConflictException(code, "A reference value with the same name already exists.");
        }
    }

    private void InvalidateLookups() => cache.Remove(LookupsCacheKey);
}
