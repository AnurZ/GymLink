using AutoMapper;
using GymLink.Application;
using GymLink.Application.Common;
using GymLink.Application.ReferenceData;
using GymLink.Domain.ReferenceData;
using GymLink.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymLink.IntegrationTests;

public sealed class ReferenceDataServiceTests
{
    [Fact]
    public async Task Crud_search_and_active_lookup_pagination_round_trip_on_sql_server()
    {
        var databaseName = $"GymLink_Phase2_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);

        try
        {
            await using var context = CreateContext(connectionString);
            await context.Database.EnsureCreatedAsync();
            using var provider = new ServiceCollection()
                .AddLogging()
                .AddGymLinkApplication()
                .BuildServiceProvider();
            var service = new ReferenceDataService(
                context,
                provider.GetRequiredService<IMapper>());

            var country = await service.CreateCountryAsync(
                new CreateCountryRequest { Code = " bih ", Name = " Bosnia and Herzegovina " },
                CancellationToken.None);
            var inactiveCountry = new Country
            {
                Code = "XX",
                Name = "Inactive country",
                IsActive = false,
            };
            context.Countries.Add(inactiveCountry);
            context.Equipment.AddRange(Enumerable.Range(0, 101).Select(index => new Equipment
            {
                Name = $"Equipment {index:D3}",
            }));
            context.Equipment.Add(new Equipment { Name = "Inactive equipment", IsActive = false });
            context.TrainingTypes.AddRange(
                new TrainingType { Name = "Strength" },
                new TrainingType { Name = "Inactive type", IsActive = false });
            await context.SaveChangesAsync();
            var city = await service.CreateCityAsync(
                new CreateCityRequest { CountryId = country.Id, Name = " Sarajevo " },
                CancellationToken.None);
            context.Cities.AddRange(
                new City { CountryId = country.Id, Name = "Inactive city", IsActive = false },
                new City { CountryId = inactiveCountry.Id, Name = "Hidden parent city" });
            await context.SaveChangesAsync();

            var countries = await service.GetActiveCountriesAsync(
                new PagedRequest(),
                CancellationToken.None);
            var cities = await service.GetActiveCitiesAsync(
                new PagedRequest(),
                CancellationToken.None);
            var firstEquipmentPage = await service.GetActiveEquipmentAsync(
                new PagedRequest { Page = 1, PageSize = PagedRequest.MaximumPageSize },
                CancellationToken.None);
            var secondEquipmentPage = await service.GetActiveEquipmentAsync(
                new PagedRequest { Page = 2, PageSize = PagedRequest.MaximumPageSize },
                CancellationToken.None);
            var trainingTypes = await service.GetActiveTrainingTypesAsync(
                new PagedRequest(),
                CancellationToken.None);
            var search = await service.SearchCitiesAsync(
                new CitySearchRequest { Query = "Sara", Page = 1, PageSize = 10 },
                CancellationToken.None);

            Assert.Equal("BIH", country.Code);
            Assert.Single(countries.Items);
            Assert.Equal(country.Id, countries.Items[0].Id);
            Assert.Single(cities.Items);
            Assert.Equal(city.Id, cities.Items[0].Id);
            Assert.Equal(101, firstEquipmentPage.TotalCount);
            Assert.Equal(PagedRequest.MaximumPageSize, firstEquipmentPage.Items.Count);
            Assert.Equal("Equipment 000", firstEquipmentPage.Items[0].Name);
            Assert.Equal("Equipment 099", firstEquipmentPage.Items[^1].Name);
            Assert.Single(secondEquipmentPage.Items);
            Assert.Equal("Equipment 100", secondEquipmentPage.Items[0].Name);
            Assert.Single(trainingTypes.Items);
            Assert.Equal("Strength", trainingTypes.Items[0].Name);
            Assert.Single(search.Items);
            Assert.Equal(1, search.TotalCount);

            var duplicate = await Assert.ThrowsAsync<ConflictException>(() =>
                service.CreateCountryAsync(
                    new CreateCountryRequest { Code = "BIH", Name = "Different name" },
                    CancellationToken.None));
            Assert.Equal("country_duplicate", duplicate.Code);

            context.ChangeTracker.Clear();
            var inUse = await Assert.ThrowsAsync<ConflictException>(() =>
                service.DeleteCountryAsync(country.Id, CancellationToken.None));
            Assert.Equal("reference_in_use", inUse.Code);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static GymLinkDbContext CreateContext(string connectionString)
    {
        var tenantContext = new TestTenantContext(null);
        var interceptor = new TenantAuditSaveChangesInterceptor(
            tenantContext,
            new TestCurrentUser(Guid.NewGuid()),
            TimeProvider.System);
        var options = new DbContextOptionsBuilder<GymLinkDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(interceptor)
            .Options;
        return new GymLinkDbContext(options, tenantContext);
    }
}
