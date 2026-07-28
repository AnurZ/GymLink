using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymLink.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5RecurringTrainerSchedules : Migration
    {
        private static readonly string[] ScheduleTenantTrainerColumns =
            ["TenantId", "TrainerProfileId"];

        private static readonly string[] WeeklyShiftUniqueColumns =
        [
            "TenantId",
            "TrainerAvailabilityScheduleId",
            "TrainerProfileId",
            "DayOfWeek",
            "Period",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppointmentReservations_AvailabilitySlotId",
                table: "AppointmentReservations");

            migrationBuilder.AlterColumn<Guid>(
                name: "AvailabilitySlotId",
                table: "AppointmentReservations",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateTable(
                name: "TrainerAvailabilitySchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrainerProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BookingHorizonWeeks = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerAvailabilitySchedules", x => x.Id);
                    table.CheckConstraint("CK_TrainerAvailabilitySchedules_BookingHorizonWeeks", "[BookingHorizonWeeks] = 8");
                    table.CheckConstraint("CK_TrainerAvailabilitySchedules_Revision", "[Revision] >= 0");
                    table.ForeignKey(
                        name: "FK_TrainerAvailabilitySchedules_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainerAvailabilitySchedules_TrainerProfiles_TrainerProfileId",
                        column: x => x.TrainerProfileId,
                        principalTable: "TrainerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrainerWeeklyShifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrainerAvailabilityScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrainerProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    Period = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StartsAtLocal = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    EndsAtLocal = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerWeeklyShifts", x => x.Id);
                    table.CheckConstraint("CK_TrainerWeeklyShifts_DayOfWeek", "[DayOfWeek] >= 0 AND [DayOfWeek] <= 6");
                    table.CheckConstraint("CK_TrainerWeeklyShifts_TimeRange", "[EndsAtLocal] > [StartsAtLocal]");
                    table.ForeignKey(
                        name: "FK_TrainerWeeklyShifts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainerWeeklyShifts_TrainerAvailabilitySchedules_TrainerAvailabilityScheduleId",
                        column: x => x.TrainerAvailabilityScheduleId,
                        principalTable: "TrainerAvailabilitySchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainerWeeklyShifts_TrainerProfiles_TrainerProfileId",
                        column: x => x.TrainerProfileId,
                        principalTable: "TrainerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReservations_AvailabilitySlotId",
                table: "AppointmentReservations",
                column: "AvailabilitySlotId",
                unique: true,
                filter: "[AvailabilitySlotId] IS NOT NULL AND [Status] IN (N'Pending', N'Confirmed')");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerAvailabilitySchedules_TenantId",
                table: "TrainerAvailabilitySchedules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerAvailabilitySchedules_TenantId_TrainerProfileId",
                table: "TrainerAvailabilitySchedules",
                columns: ScheduleTenantTrainerColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainerAvailabilitySchedules_TrainerProfileId",
                table: "TrainerAvailabilitySchedules",
                column: "TrainerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerWeeklyShifts_TenantId",
                table: "TrainerWeeklyShifts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerWeeklyShifts_TenantId_TrainerAvailabilityScheduleId_TrainerProfileId_DayOfWeek_Period",
                table: "TrainerWeeklyShifts",
                columns: WeeklyShiftUniqueColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainerWeeklyShifts_TrainerAvailabilityScheduleId",
                table: "TrainerWeeklyShifts",
                column: "TrainerAvailabilityScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerWeeklyShifts_TrainerProfileId",
                table: "TrainerWeeklyShifts",
                column: "TrainerProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrainerWeeklyShifts");

            migrationBuilder.DropTable(
                name: "TrainerAvailabilitySchedules");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentReservations_AvailabilitySlotId",
                table: "AppointmentReservations");

            migrationBuilder.AlterColumn<Guid>(
                name: "AvailabilitySlotId",
                table: "AppointmentReservations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReservations_AvailabilitySlotId",
                table: "AppointmentReservations",
                column: "AvailabilitySlotId",
                unique: true,
                filter: "[Status] IN (N'Pending', N'Confirmed')");
        }
    }
}
