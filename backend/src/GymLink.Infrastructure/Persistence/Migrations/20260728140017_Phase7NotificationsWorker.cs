using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymLink.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7NotificationsWorker : Migration
    {
        private static readonly string[] InboxRetryColumns =
            ["CompletedAtUtc", "NextAttemptAtUtc"];

        private static readonly string[] InboxIdentityColumns =
            ["MessageId", "Consumer"];

        private static readonly string[] OutboxCorrelationColumns =
            ["CorrelationId", "OccurredAtUtc"];

        private static readonly string[] OutboxRetryColumns =
            ["PublishedAtUtc", "NextAttemptAtUtc", "LeasedUntilUtc"];

        private static readonly string[] ResetRequestColumns =
            ["UserId", "RequestedAtUtc"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Notifications",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: Array.Empty<byte>());

            migrationBuilder.AddColumn<Guid>(
                name: "SourceMessageId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Consumer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessingAttempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => x.Id);
                    table.CheckConstraint("CK_InboxMessages_ProcessingAttempts", "[ProcessingAttempts] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContractVersion = table.Column<int>(type: "int", nullable: false),
                    RoutingKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", maxLength: 32000, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeasedUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishAttempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_OutboxMessages_ContractVersion", "[ContractVersion] > 0");
                    table.CheckConstraint("CK_OutboxMessages_PublishAttempts", "[PublishAttempts] >= 0");
                    table.ForeignKey(
                        name: "FK_OutboxMessages_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PasswordResetChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CodeSalt = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    LastFailedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SupersededAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestIpHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetChallenges", x => x.Id);
                    table.CheckConstraint("CK_PasswordResetChallenges_FailedAttempts", "[FailedAttempts] >= 0 AND [FailedAttempts] <= 5");
                    table.CheckConstraint("CK_PasswordResetChallenges_TerminalState", "[ConsumedAtUtc] IS NULL OR [SupersededAtUtc] IS NULL");
                    table.CheckConstraint("CK_PasswordResetChallenges_TimeRange", "[ExpiresAtUtc] > [RequestedAtUtc]");
                    table.ForeignKey(
                        name: "FK_PasswordResetChallenges_UserProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_SourceMessageId",
                table: "Notifications",
                column: "SourceMessageId",
                unique: true,
                filter: "[SourceMessageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_CompletedAtUtc_NextAttemptAtUtc",
                table: "InboxMessages",
                columns: InboxRetryColumns,
                filter: "[CompletedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_MessageId_Consumer",
                table: "InboxMessages",
                columns: InboxIdentityColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_CorrelationId_OccurredAtUtc",
                table: "OutboxMessages",
                columns: OutboxCorrelationColumns);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_PublishedAtUtc_NextAttemptAtUtc_LeasedUntilUtc",
                table: "OutboxMessages",
                columns: OutboxRetryColumns,
                filter: "[PublishedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_TenantId",
                table: "OutboxMessages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetChallenges_ExpiresAtUtc",
                table: "PasswordResetChallenges",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetChallenges_UserId",
                table: "PasswordResetChallenges",
                column: "UserId",
                unique: true,
                filter: "[ConsumedAtUtc] IS NULL AND [SupersededAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetChallenges_UserId_RequestedAtUtc",
                table: "PasswordResetChallenges",
                columns: ResetRequestColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxMessages");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "PasswordResetChallenges");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_SourceMessageId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "SourceMessageId",
                table: "Notifications");
        }
    }
}
