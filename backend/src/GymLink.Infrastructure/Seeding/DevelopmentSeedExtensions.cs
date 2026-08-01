using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using GymLink.Application.Recommendations;

namespace GymLink.Infrastructure.Seeding;

public static class DevelopmentSeedExtensions
{
    public static async Task SeedDevelopmentDataAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptions<DevelopmentSeedOptions>>()
            .Value;
        if (options.Enabled && !app.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Development seeding cannot be enabled outside the Development environment.");
        }

        if (options.Enabled)
        {
            var memberIds = await scope.ServiceProvider
                .GetRequiredService<DevelopmentDataSeeder>()
                .SeedAsync(cancellationToken);
            await scope.ServiceProvider
                .GetRequiredService<IRecommendationService>()
                .GenerateForUsersAsync(memberIds, cancellationToken);
        }
    }
}
