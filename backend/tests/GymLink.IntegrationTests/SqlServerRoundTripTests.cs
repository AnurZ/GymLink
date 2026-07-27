using GymLink.Domain.Catalog;
using GymLink.Domain.ReferenceData;
using GymLink.Domain.Tenancy;
using GymLink.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymLink.IntegrationTests;

public sealed class SqlServerRoundTripTests
{
    [Fact]
    public async Task SqlServerRoundTripEnforcesTenantFilterAndAuditGuard()
    {
        var databaseName = $"GymLink_Phase1_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Integrated Security=true;TrustServerCertificate=true";
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userId = Guid.NewGuid();

        try
        {
            await using (var setup = CreateContext(connectionString, tenantA, userId))
            {
                await setup.Database.EnsureCreatedAsync();
                var country = new Country { Code = "BIH", Name = "Bosnia and Herzegovina" };
                var city = new City { CountryId = country.Id, Name = "Sarajevo" };
                setup.AddRange(
                    country,
                    city,
                    new Tenant(tenantA, "Tenant A"),
                    new Tenant(tenantB, "Tenant B"));
                await setup.SaveChangesAsync();

                setup.Add(new Gym
                {
                    Name = "Gym A",
                    Description = "Tenant A gym",
                    Address = "Address A",
                    CityId = city.Id,
                });
                await setup.SaveChangesAsync();
            }

            await using (var tenantBContext = CreateContext(connectionString, tenantB, userId))
            {
                var cityId = await tenantBContext.Cities.Select(x => x.Id).SingleAsync();
                tenantBContext.Add(new Gym
                {
                    Name = "Gym B",
                    Description = "Tenant B gym",
                    Address = "Address B",
                    CityId = cityId,
                });
                await tenantBContext.SaveChangesAsync();
            }

            await using var tenantAContext = CreateContext(connectionString, tenantA, userId);
            var visibleGyms = await tenantAContext.Gyms.AsNoTracking().ToListAsync();

            Assert.Single(visibleGyms);
            Assert.Equal("Gym A", visibleGyms[0].Name);
            Assert.Equal(tenantA, visibleGyms[0].TenantId);
            Assert.Equal(userId, visibleGyms[0].CreatedByUserId);
            Assert.Equal(DateTimeKind.Utc, visibleGyms[0].CreatedAtUtc.Kind);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString, null, null);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static GymLinkDbContext CreateContext(
        string connectionString,
        Guid? tenantId,
        Guid? userId)
    {
        var tenantContext = new TestTenantContext(tenantId);
        var interceptor = new TenantAuditSaveChangesInterceptor(
            tenantContext,
            new TestCurrentUser(userId),
            TimeProvider.System);
        var options = new DbContextOptionsBuilder<GymLinkDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(interceptor)
            .Options;
        return new GymLinkDbContext(options, tenantContext);
    }
}
