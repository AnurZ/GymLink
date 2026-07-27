using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymLink.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase4MembershipWorkflows : Migration
    {
        private static readonly string[] MembershipKeyColumns =
            ["TenantId", "MemberUserId", "GymId"];
        private static readonly string[] PreviousMembershipIndexColumns =
            ["TenantId", "MemberUserId", "GymId", "Status"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Memberships_TenantId_MemberUserId_GymId_Status",
                table: "Memberships");

            migrationBuilder.AddColumn<DateTime>(
                name: "StatusChangedAtUtc",
                table: "Memberships",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StatusChangedByUserId",
                table: "Memberships",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusReason",
                table: "Memberships",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_StatusChangedByUserId",
                table: "Memberships",
                column: "StatusChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_TenantId_MemberUserId_GymId",
                table: "Memberships",
                columns: MembershipKeyColumns,
                unique: true,
                filter: "[Status] IN (N'PendingPayment', N'Active', N'Suspended')");

            migrationBuilder.AddForeignKey(
                name: "FK_Memberships_UserProfiles_StatusChangedByUserId",
                table: "Memberships",
                column: "StatusChangedByUserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Memberships_UserProfiles_StatusChangedByUserId",
                table: "Memberships");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_StatusChangedByUserId",
                table: "Memberships");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_TenantId_MemberUserId_GymId",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "StatusChangedAtUtc",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "StatusChangedByUserId",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "StatusReason",
                table: "Memberships");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_TenantId_MemberUserId_GymId_Status",
                table: "Memberships",
                columns: PreviousMembershipIndexColumns);
        }
    }
}
