using AutoMapper;
using GymLink.Application;
using GymLink.Application.Common;
using GymLink.Application.ReferenceData;
using GymLink.Domain.ReferenceData;
using GymLink.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace GymLink.IntegrationTests;

public sealed class ReferenceDataServiceTests
{
    [Fact]
    public async Task Crud_search_and_lookup_cache_invalidation_round_trip_on_sql_server()
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
                provider.GetRequiredService<IMapper>(),
                provider.GetRequiredService<IMemoryCache>());

            var country = await service.CreateCountryAsync(
                new CreateCountryRequest { Code = " bih ", Name = " Bosnia and Herzegovina " },
                CancellationToken.None);
            context.Equipment.AddRange(Enumerable.Range(0, 101).Select(index => new Equipment
            {
                Name = $"Equipment {index:D3}",
            }));
            await context.SaveChangesAsync();
            var initialLookups = await service.GetActiveLookupsAsync(CancellationToken.None);
            var city = await service.CreateCityAsync(
                new CreateCityRequest { CountryId = country.Id, Name = " Sarajevo " },
                CancellationToken.None);
            var refreshedLookups = await service.GetActiveLookupsAsync(CancellationToken.None);
            var search = await service.SearchCitiesAsync(
                new CitySearchRequest { Query = "Sara", Page = 1, PageSize = 10 },
                CancellationToken.None);

            Assert.Equal("BIH", country.Code);
            Assert.Equal(PagedRequest.MaximumPageSize, initialLookups.Equipment.Count);
            Assert.Equal("Equipment 000", initialLookups.Equipment[0].Name);
            Assert.Equal("Equipment 099", initialLookups.Equipment[^1].Name);
            Assert.Empty(initialLookups.Cities);
            Assert.Single(refreshedLookups.Cities);
            Assert.Equal(city.Id, refreshedLookups.Cities[0].Id);
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
