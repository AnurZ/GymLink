using DotNetEnv;
using GymLink.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GymLink.Infrastructure.Persistence;

public sealed class GymLinkDbContextFactory : IDesignTimeDbContextFactory<GymLinkDbContext>
{
    public GymLinkDbContext CreateDbContext(string[] args)
    {
        Env.TraversePath().NoClobber().Load();
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__GymLink")
            ?? throw new InvalidOperationException(
                "Environment variable ConnectionStrings__GymLink is required for EF tooling.");
        var options = new DbContextOptionsBuilder<GymLinkDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new GymLinkDbContext(options, DesignTimeTenantContext.Instance);
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public static DesignTimeTenantContext Instance { get; } = new();
        public Guid? TenantId => null;
        public string? TenantRole => null;
        public bool HasTenant => false;
    }
}
