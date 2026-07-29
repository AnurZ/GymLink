using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF migration operations require inline column arrays.

namespace GymLink.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8StripeHostedCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_TenantId_Purpose_TargetId_Status",
                table: "Payments");

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FailedAtUtc",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureCode",
                table: "Payments",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderSessionId",
                table: "Payments",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "StartsAtUtc",
                table: "Memberships",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<DateTime>(
                name: "EndsAtUtc",
                table: "Memberships",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddColumn<int>(
                name: "DurationDays",
                table: "Memberships",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [Memberships]
                SET [DurationDays] = DATEDIFF(DAY, [StartsAtUtc], [EndsAtUtc])
                WHERE [DurationDays] IS NULL;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "DurationDays",
                table: "Memberships",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentDueAtUtc",
                table: "AppointmentReservations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StripeEventReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderEventId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ProviderObjectId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeEventReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StripeEventReceipts_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StripeEventReceipts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ProviderSessionId",
                table: "Payments",
                column: "ProviderSessionId",
                unique: true,
                filter: "[ProviderSessionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Status_ExpiresAtUtc",
                table: "Payments",
                columns: new[] { "Status", "ExpiresAtUtc" },
                filter: "[Status] = N'Processing'");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_Purpose_TargetId",
                table: "Payments",
                columns: new[] { "TenantId", "Purpose", "TargetId" },
                unique: true,
                filter: "[Status] IN (N'Created', N'Processing', N'Succeeded')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Amount",
                table: "Payments",
                sql: "[Amount] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_ChargedAmount",
                table: "Payments",
                sql: "[ChargedAmount] IS NULL OR [ChargedAmount] = [Amount]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Memberships_ActivationDates",
                table: "Memberships",
                sql: "([StartsAtUtc] IS NULL AND [EndsAtUtc] IS NULL) OR ([StartsAtUtc] IS NOT NULL AND [EndsAtUtc] > [StartsAtUtc])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Memberships_ActiveDatesRequired",
                table: "Memberships",
                sql: "[Status] NOT IN (N'Active', N'Suspended', N'Expired') OR ([StartsAtUtc] IS NOT NULL AND [EndsAtUtc] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Memberships_DurationDays",
                table: "Memberships",
                sql: "[DurationDays] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentReservations_PaymentDeadline",
                table: "AppointmentReservations",
                sql: "[PaymentDueAtUtc] IS NULL OR [PaymentDueAtUtc] < [StartsAtUtc]");

            migrationBuilder.CreateIndex(
                name: "IX_StripeEventReceipts_PaymentId_ReceivedAtUtc",
                table: "StripeEventReceipts",
                columns: new[] { "PaymentId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StripeEventReceipts_ProviderEventId",
                table: "StripeEventReceipts",
                column: "ProviderEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StripeEventReceipts_ProviderObjectId_EventType",
                table: "StripeEventReceipts",
                columns: new[] { "ProviderObjectId", "EventType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StripeEventReceipts_TenantId",
                table: "StripeEventReceipts",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StripeEventReceipts");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ProviderSessionId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_Status_ExpiresAtUtc",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_TenantId_Purpose_TargetId",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Amount",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_ChargedAmount",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Memberships_ActivationDates",
                table: "Memberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Memberships_ActiveDatesRequired",
                table: "Memberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Memberships_DurationDays",
                table: "Memberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentReservations_PaymentDeadline",
                table: "AppointmentReservations");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "FailedAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "FailureCode",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ProviderSessionId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "DurationDays",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "PaymentDueAtUtc",
                table: "AppointmentReservations");

            migrationBuilder.AlterColumn<DateTime>(
                name: "StartsAtUtc",
                table: "Memberships",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "EndsAtUtc",
                table: "Memberships",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_Purpose_TargetId_Status",
                table: "Payments",
                columns: new[] { "TenantId", "Purpose", "TargetId", "Status" });
        }
    }
}
