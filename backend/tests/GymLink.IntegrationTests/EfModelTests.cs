using GymLink.Domain.Common;
using GymLink.Domain.Memberships;
using GymLink.Domain.Payments;
using GymLink.Domain.Reservations;
using GymLink.Domain.Tenancy;
using GymLink.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GymLink.IntegrationTests;

public sealed class EfModelTests
{
    [Fact]
    public void Model_has_explicit_primary_keys_and_tenant_filters()
    {
        using var context = CreateContext(Guid.NewGuid());

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            Assert.NotNull(entityType.FindPrimaryKey());

            if (typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                Assert.NotEmpty(entityType.GetDeclaredQueryFilters());
                Assert.NotNull(entityType.FindProperty(nameof(ITenantOwned.TenantId)));
                Assert.Contains(
                    entityType.GetForeignKeys(),
                    foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Tenant));
            }
        }
    }

    [Fact]
    public void Mutable_roots_use_rowversion_concurrency()
    {
        using var context = CreateContext(Guid.NewGuid());
        var trackedTypes = context.Model.GetEntityTypes()
            .Where(x => typeof(IConcurrencyTracked).IsAssignableFrom(x.ClrType));

        Assert.NotEmpty(trackedTypes);
        foreach (var entityType in trackedTypes)
        {
            var property = entityType.FindProperty(nameof(IConcurrencyTracked.RowVersion));
            Assert.NotNull(property);
            Assert.True(property.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        }
    }

    [Fact]
    public void Money_columns_have_explicit_precision()
    {
        using var context = CreateContext(Guid.NewGuid());

        AssertPrecision(context, typeof(Payment), nameof(Payment.Amount), 18, 2);
        AssertPrecision(context, typeof(Refund), nameof(Refund.Amount), 18, 2);
        AssertPrecision(context, typeof(Membership), nameof(Membership.Price), 18, 2);
        AssertPrecision(
            context,
            typeof(AppointmentReservation),
            nameof(AppointmentReservation.Price),
            18,
            2);
    }

    [Fact]
    public void Critical_duplicate_protections_exist()
    {
        using var context = CreateContext(Guid.NewGuid());

        Assert.Contains(
            context.Model.FindEntityType(typeof(UserGymAssignment))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["TenantId", "UserId", "Role"]));
        Assert.Contains(
            context.Model.FindEntityType(typeof(MembershipRequest))!.GetIndexes(),
            index => index.IsUnique && index.GetFilter() is not null);
        Assert.Contains(
            context.Model.FindEntityType(typeof(Review))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["ReservationId"]));
    }

    private static GymLinkDbContext CreateContext(Guid? tenantId)
    {
        var options = new DbContextOptionsBuilder<GymLinkDbContext>()
            .UseSqlServer(TestSqlServer.ConnectionString("GymLinkModelOnly"))
            .Options;
        return new GymLinkDbContext(options, new TestTenantContext(tenantId));
    }

    private static void AssertPrecision(
        GymLinkDbContext context,
        Type entityType,
        string propertyName,
        int precision,
        int scale)
    {
        var property = context.Model.FindEntityType(entityType)!.FindProperty(propertyName)!;
        Assert.Equal(precision, property.GetPrecision());
        Assert.Equal(scale, property.GetScale());
    }

    private static IEnumerable<string> PropertyNames(IReadOnlyIndex index) =>
        index.Properties.Select(x => x.Name);
}
