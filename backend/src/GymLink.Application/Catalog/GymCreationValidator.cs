using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Catalog;

internal sealed record ValidatedGymIdentity(string Name, string Address);

internal interface IGymCreationValidator
{
    Task<ValidatedGymIdentity> ValidateAsync(
        Guid cityId,
        string name,
        string address,
        CancellationToken cancellationToken);
}

internal sealed class GymCreationValidator(IApplicationDbContext dbContext)
    : IGymCreationValidator
{
    public async Task<ValidatedGymIdentity> ValidateAsync(
        Guid cityId,
        string name,
        string address,
        CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim();
        var normalizedAddress = address.Trim();
        var cityExists = await (
                from city in dbContext.Cities.AsNoTracking()
                join country in dbContext.Countries.AsNoTracking()
                    on city.CountryId equals country.Id
                where city.Id == cityId &&
                      city.IsActive &&
                      country.IsActive &&
                      country.Code == "BIH"
                select city.Id)
            .AnyAsync(cancellationToken);
        if (!cityExists)
        {
            throw new NotFoundException(
                "city_not_found",
                "The selected active BiH city was not found.");
        }

        if (await dbContext.Gyms.IgnoreQueryFilters().AsNoTracking().AnyAsync(
                gym => gym.CityId == cityId &&
                       gym.Name == normalizedName &&
                       gym.Address == normalizedAddress,
                cancellationToken))
        {
            throw new ConflictException(
                "gym_already_exists",
                "A gym with the same name and address already exists.");
        }

        return new(normalizedName, normalizedAddress);
    }
}
