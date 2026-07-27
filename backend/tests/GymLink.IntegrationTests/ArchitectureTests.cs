using System.Reflection;
using GymLink.Application.Common;
using GymLink.Domain.Common;
using GymLink.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymLink.IntegrationTests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Domain_does_not_reference_outer_layers()
    {
        var references = typeof(Entity).Assembly.GetReferencedAssemblies()
            .Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("GymLink.Application", references);
        Assert.DoesNotContain("GymLink.Infrastructure", references);
        Assert.DoesNotContain("GymLink.Api", references);
    }

    [Fact]
    public void Application_does_not_reference_infrastructure_or_api()
    {
        var references = typeof(PagedRequest).Assembly.GetReferencedAssemblies()
            .Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("GymLink.Infrastructure", references);
        Assert.DoesNotContain("GymLink.Api", references);
    }

    [Fact]
    public void Every_persisted_entity_has_a_dedicated_configuration()
    {
        var infrastructureAssembly = typeof(GymLinkDbContext).Assembly;
        var configuredTypes = infrastructureAssembly.GetTypes()
            .SelectMany(type => type.GetInterfaces()
                .Where(x => x.IsGenericType &&
                    x.GetGenericTypeDefinition().FullName ==
                    "Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1")
                .Select(x => x.GenericTypeArguments[0]))
            .ToHashSet();

        using var context = new GymLinkDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<GymLinkDbContext>()
                .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=GymLinkArchitectureOnly;Integrated Security=true")
                .Options,
            new TestTenantContext(Guid.NewGuid()));

        Assert.All(
            context.Model.GetEntityTypes(),
            entityType => Assert.Contains(entityType.ClrType, configuredTypes));
    }
}
