using GymLink.Domain.Catalog;
using GymLink.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymLink.IntegrationTests;

public sealed class TenantGuardTests
{
    [Fact]
    public void New_tenant_entity_is_stamped_and_audited()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var gym = new Gym { Name = "Gym", Description = "Description", Address = "Address" };
        context.Add(gym);
        var interceptor = new TenantAuditSaveChangesInterceptor(
            new TestTenantContext(tenantId),
            new TestCurrentUser(userId),
            TimeProvider.System);

        interceptor.GuardAndStamp(context);

        Assert.Equal(tenantId, gym.TenantId);
        Assert.Equal(userId, gym.CreatedByUserId);
        Assert.Equal(DateTimeKind.Utc, gym.CreatedAtUtc.Kind);
    }

    [Fact]
    public void Missing_tenant_context_fails_closed()
    {
        using var context = CreateContext(null);
        context.Add(new Gym { Name = "Gym", Description = "Description", Address = "Address" });
        var interceptor = new TenantAuditSaveChangesInterceptor(
            new TestTenantContext(null),
            new TestCurrentUser(Guid.NewGuid()),
            TimeProvider.System);

        Assert.Throws<InvalidOperationException>(() => interceptor.GuardAndStamp(context));
    }

    [Fact]
    public void Mismatched_tenant_is_rejected()
    {
        using var context = CreateContext(Guid.NewGuid());
        context.Add(new Gym
        {
            TenantId = Guid.NewGuid(),
            Name = "Gym",
            Description = "Description",
            Address = "Address",
        });
        var interceptor = new TenantAuditSaveChangesInterceptor(
            new TestTenantContext(Guid.NewGuid()),
            new TestCurrentUser(Guid.NewGuid()),
            TimeProvider.System);

        Assert.Throws<InvalidOperationException>(() => interceptor.GuardAndStamp(context));
    }

    private static GymLinkDbContext CreateContext(Guid? tenantId)
    {
        var options = new DbContextOptionsBuilder<GymLinkDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=GymLinkGuardOnly;Integrated Security=true")
            .Options;
        return new GymLinkDbContext(options, new TestTenantContext(tenantId));
    }
}
