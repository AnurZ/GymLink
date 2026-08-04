using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GymLink.Infrastructure.Persistence;

public static class DatabaseStartupExtensions
{
    private const int MaximumAttempts = 5;
    private static readonly Action<ILogger, int, int, double, Exception?> LogRetry =
        LoggerMessage.Define<int, int, double>(
            LogLevel.Warning,
            new EventId(600, "DatabaseStartupMigrationRetry"),
            "Database migration failed on attempt {Attempt}/{MaximumAttempts}; retrying in {DelaySeconds} seconds.");

    public static async Task MigrateDatabaseOnStartupAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        var settings = app.Services
            .GetRequiredService<IOptions<DatabaseStartupOptions>>()
            .Value;
        if (!settings.MigrateOnStartup)
        {
            return;
        }

        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("DatabaseStartupMigration");
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GymLinkDbContext>();
            try
            {
                await db.Database.MigrateAsync(cancellationToken);
                return;
            }
            catch (SqlException exception) when (attempt < MaximumAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                LogRetry(
                    logger,
                    attempt,
                    MaximumAttempts,
                    delay.TotalSeconds,
                    exception);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}
