using GymLink.Application.Abstractions;
using GymLink.Infrastructure.Persistence;
using GymLink.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GymLink.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGymLinkInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("GymLink")
            ?? throw new InvalidOperationException(
                "Environment variable ConnectionStrings__GymLink is required.");

        services.AddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, FailClosedRequestContext>();
        services.TryAddScoped<ITenantContext, FailClosedRequestContext>();
        services.AddScoped<TenantAuditSaveChangesInterceptor>();
        services.AddScoped<IApplicationDbContext>(provider =>
            provider.GetRequiredService<GymLinkDbContext>());
        services.AddDbContext<GymLinkDbContext>((provider, options) =>
            options.UseSqlServer(connectionString)
                .AddInterceptors(provider.GetRequiredService<TenantAuditSaveChangesInterceptor>()));

        return services;
    }
}
