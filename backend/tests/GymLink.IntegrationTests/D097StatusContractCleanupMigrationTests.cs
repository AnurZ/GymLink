using System.Data;
using System.Globalization;
using GymLink.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GymLink.IntegrationTests;

public sealed class D097StatusContractCleanupMigrationTests
{
    private const string PreviousMigration =
        "20260902144713_Review08RemoveDormantRefundPersistence";

    [Fact]
    public async Task Latest_migration_normalizes_all_retired_status_values()
    {
        var connectionString = TestSqlServer.ConnectionString(
            $"GymLink_D097_{Guid.NewGuid():N}");

        try
        {
            await using var context = CreateContext(connectionString);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);

            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE [GymRegistrationRequests] NOCHECK CONSTRAINT ALL;
                ALTER TABLE [UserGymAssignments] NOCHECK CONSTRAINT ALL;
                ALTER TABLE [MembershipRequests] NOCHECK CONSTRAINT ALL;

                INSERT INTO [GymRegistrationRequests]
                    ([Id], [ApplicantUserId], [ProposedGymName], [ProposedDescription],
                     [ProposedAddress], [CityId], [Latitude], [Longitude], [Status],
                     [CreatedAtUtc])
                VALUES
                    ('00000000-0000-0000-0000-000000000097', NEWID(), N'Legacy gym',
                     N'Legacy draft', N'Legacy address', NEWID(), 43.0, 18.0,
                     N'Draft', '2026-01-01T00:00:00Z');

                INSERT INTO [UserGymAssignments]
                    ([Id], [TenantId], [UserId], [Role], [Status], [StartsAtUtc],
                     [CreatedAtUtc])
                VALUES
                    ('00000000-0000-0000-0000-000000000098', NEWID(), NEWID(),
                     N'Member', N'Invited', '2026-01-02T00:00:00Z',
                     '2026-01-02T00:00:00Z'),
                    ('00000000-0000-0000-0000-000000000099', NEWID(), NEWID(),
                     N'Trainer', N'Suspended', '2026-01-03T00:00:00Z',
                     '2026-01-03T00:00:00Z');

                INSERT INTO [MembershipRequests]
                    ([Id], [TenantId], [MemberUserId], [GymId], [MembershipPlanId],
                     [PaymentMethod], [Status], [RequestedAtUtc], [CreatedAtUtc])
                VALUES
                    ('00000000-0000-0000-0000-000000000100', NEWID(), NEWID(),
                     NEWID(), NEWID(), N'StripeFallback', N'Approved',
                     '2026-01-04T00:00:00Z', '2026-01-04T00:00:00Z');
                """);

            await migrator.MigrateAsync();

            Assert.Equal(
                "Rejected",
                await ScalarAsync(
                    context,
                    "SELECT [Status] FROM [GymRegistrationRequests] " +
                    "WHERE [Id] = '00000000-0000-0000-0000-000000000097'"));
            Assert.Equal(
                2,
                Convert.ToInt32(
                    await ScalarAsync(
                        context,
                        "SELECT COUNT(*) FROM [UserGymAssignments] " +
                        "WHERE [Status] = N'Ended' AND [EndsAtUtc] IS NOT NULL"),
                    CultureInfo.InvariantCulture));
            Assert.Equal(
                "Stripe",
                await ScalarAsync(
                    context,
                    "SELECT [PaymentMethod] FROM [MembershipRequests] " +
                    "WHERE [Id] = '00000000-0000-0000-0000-000000000100'"));
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());

            await context.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM [MembershipRequests]
                WHERE [Id] = '00000000-0000-0000-0000-000000000100';
                DELETE FROM [UserGymAssignments]
                WHERE [Id] IN (
                    '00000000-0000-0000-0000-000000000098',
                    '00000000-0000-0000-0000-000000000099');
                DELETE FROM [GymRegistrationRequests]
                WHERE [Id] = '00000000-0000-0000-0000-000000000097';

                ALTER TABLE [GymRegistrationRequests] WITH CHECK CHECK CONSTRAINT ALL;
                ALTER TABLE [UserGymAssignments] WITH CHECK CHECK CONSTRAINT ALL;
                ALTER TABLE [MembershipRequests] WITH CHECK CHECK CONSTRAINT ALL;
                """);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<object?> ScalarAsync(
        GymLinkDbContext context,
        string commandText)
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
            command.CommandText = commandText;
            return await command.ExecuteScalarAsync();
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
