using System.Data;
using System.Globalization;
using GymLink.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GymLink.IntegrationTests;

public sealed class Review08RefundPersistenceMigrationTests
{
    private const string PreviousMigration = "20260813194726_D070ChatMessageImages";

    [Fact]
    public async Task Latest_migration_removes_the_dormant_refunds_table()
    {
        var connectionString = TestSqlServer.ConnectionString(
            $"GymLink_Review08_{Guid.NewGuid():N}");

        try
        {
            await using var context = CreateContext(connectionString);
            var migrator = context.GetService<IMigrator>();

            await migrator.MigrateAsync(PreviousMigration);
            Assert.True(await RefundsTableExistsAsync(context));

            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE [Refunds] NOCHECK CONSTRAINT ALL;
                INSERT INTO [Refunds]
                    ([Id], [Amount], [CreatedAtUtc], [Currency], [IdempotencyKey],
                     [PaymentId], [Reason], [RequestedAtUtc], [RequestedByUserId],
                     [Status], [TenantId])
                VALUES
                    (NEWID(), 1, SYSUTCDATETIME(), N'BAM', N'review-08-anomaly',
                     NEWID(), N'Unexpected dormant row', SYSUTCDATETIME(), NEWID(),
                     N'Created', NEWID());
                """);
            var anomaly = await Assert.ThrowsAnyAsync<Exception>(
                () => migrator.MigrateAsync());
            Assert.Contains("Unexpected Refunds rows", anomaly.ToString());
            await context.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM [Refunds] WHERE [IdempotencyKey] = N'review-08-anomaly';
                ALTER TABLE [Refunds] WITH CHECK CHECK CONSTRAINT ALL;
                """);

            await migrator.MigrateAsync();

            Assert.False(await RefundsTableExistsAsync(context));
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<bool> RefundsTableExistsAsync(GymLinkDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT CASE WHEN OBJECT_ID(N'[dbo].[Refunds]', N'U') IS NULL " +
                "THEN 0 ELSE 1 END";
            return Convert.ToInt32(
                await command.ExecuteScalarAsync(),
                CultureInfo.InvariantCulture) == 1;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static GymLinkDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GymLinkDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new GymLinkDbContext(options, new TestTenantContext(null));
    }
}
