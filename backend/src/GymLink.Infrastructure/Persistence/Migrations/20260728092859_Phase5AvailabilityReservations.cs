using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymLink.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5AvailabilityReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppointmentReservations_AvailabilitySlotId",
                table: "AppointmentReservations");

            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM [AppointmentReservations]
                    WHERE [MembershipId] IS NULL OR [AvailabilitySlotId] IS NULL
                )
                    THROW 51000, 'Phase 5 requires every reservation to reference a membership and availability slot.', 1;
                """);

            migrationBuilder.AddColumn<decimal>(
                name: "AverageRating",
                table: "Gyms",
                type: "decimal(3,2)",
                precision: 3,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ReviewCount",
                table: "Gyms",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<Guid>(
                name: "MembershipId",
                table: "AppointmentReservations",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "AvailabilitySlotId",
                table: "AppointmentReservations",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "AppointmentReservations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompletedByUserId",
                table: "AppointmentReservations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAtUtc",
                table: "AppointmentReservations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedByUserId",
                table: "AppointmentReservations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GymReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GymId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GymReviews", x => x.Id);
                    table.CheckConstraint("CK_GymReviews_Rating", "[Rating] >= 1 AND [Rating] <= 5");
                    table.ForeignKey(
                        name: "FK_GymReviews_Gyms_GymId",
                        column: x => x.GymId,
                        principalTable: "Gyms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GymReviews_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GymReviews_UserProfiles_ReviewerUserId",
                        column: x => x.ReviewerUserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrainerProfiles_AverageRating",
                table: "TrainerProfiles",
                sql: "[AverageRating] >= 0 AND [AverageRating] <= 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrainerProfiles_ReviewCount",
                table: "TrainerProfiles",
                sql: "[ReviewCount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrainerAvailabilitySlots_Capacity",
                table: "TrainerAvailabilitySlots",
                sql: "[Capacity] = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrainerAvailabilitySlots_TimeRange",
                table: "TrainerAvailabilitySlots",
                sql: "[EndsAtUtc] > [StartsAtUtc]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reviews_Rating",
                table: "Reviews",
                sql: "[Rating] >= 1 AND [Rating] <= 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Gyms_AverageRating",
                table: "Gyms",
                sql: "[AverageRating] >= 0 AND [AverageRating] <= 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Gyms_ReviewCount",
                table: "Gyms",
                sql: "[ReviewCount] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReservations_AvailabilitySlotId",
                table: "AppointmentReservations",
                column: "AvailabilitySlotId",
                unique: true,
                filter: "[Status] IN (N'Pending', N'Confirmed')");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReservations_CompletedByUserId",
                table: "AppointmentReservations",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReservations_ConfirmedByUserId",
                table: "AppointmentReservations",
                column: "ConfirmedByUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentReservations_TimeRange",
                table: "AppointmentReservations",
                sql: "[EndsAtUtc] > [StartsAtUtc]");

            migrationBuilder.CreateIndex(
                name: "IX_GymReviews_GymId",
                table: "GymReviews",
                column: "GymId");

            migrationBuilder.CreateIndex(
                name: "IX_GymReviews_ReviewerUserId",
                table: "GymReviews",
                column: "ReviewerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GymReviews_TenantId",
                table: "GymReviews",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_GymReviews_TenantId_GymId_ReviewerUserId",
                table: "GymReviews",
                columns: ["TenantId", "GymId", "ReviewerUserId"],
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AppointmentReservations_UserProfiles_CompletedByUserId",
                table: "AppointmentReservations",
                column: "CompletedByUserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppointmentReservations_UserProfiles_ConfirmedByUserId",
                table: "AppointmentReservations",
                column: "ConfirmedByUserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppointmentReservations_UserProfiles_CompletedByUserId",
                table: "AppointmentReservations");

            migrationBuilder.DropForeignKey(
                name: "FK_AppointmentReservations_UserProfiles_ConfirmedByUserId",
                table: "AppointmentReservations");

            migrationBuilder.DropTable(
                name: "GymReviews");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TrainerProfiles_AverageRating",
                table: "TrainerProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TrainerProfiles_ReviewCount",
                table: "TrainerProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TrainerAvailabilitySlots_Capacity",
                table: "TrainerAvailabilitySlots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TrainerAvailabilitySlots_TimeRange",
                table: "TrainerAvailabilitySlots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Reviews_Rating",
                table: "Reviews");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Gyms_AverageRating",
                table: "Gyms");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Gyms_ReviewCount",
                table: "Gyms");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentReservations_AvailabilitySlotId",
                table: "AppointmentReservations");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentReservations_CompletedByUserId",
                table: "AppointmentReservations");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentReservations_ConfirmedByUserId",
                table: "AppointmentReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentReservations_TimeRange",
                table: "AppointmentReservations");

            migrationBuilder.DropColumn(
                name: "AverageRating",
                table: "Gyms");

            migrationBuilder.DropColumn(
                name: "ReviewCount",
                table: "Gyms");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "AppointmentReservations");

            migrationBuilder.DropColumn(
                name: "CompletedByUserId",
                table: "AppointmentReservations");

            migrationBuilder.DropColumn(
                name: "ConfirmedAtUtc",
                table: "AppointmentReservations");

            migrationBuilder.DropColumn(
                name: "ConfirmedByUserId",
                table: "AppointmentReservations");

            migrationBuilder.AlterColumn<Guid>(
                name: "MembershipId",
                table: "AppointmentReservations",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "AvailabilitySlotId",
                table: "AppointmentReservations",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReservations_AvailabilitySlotId",
                table: "AppointmentReservations",
                column: "AvailabilitySlotId");
        }
    }
}
