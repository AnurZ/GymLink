using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF migration operations use generated column arrays.

#nullable disable

namespace GymLink.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase10RecommendationIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM [UserPreferences] WHERE [PreferredCityId] IS NULL OR [PreferredTrainingTypeId] IS NULL;");
            migrationBuilder.Sql(
                "DELETE FROM [Recommendations] WHERE [TargetTenantId] IS NULL;");

            migrationBuilder.DropIndex(
                name: "IX_UserPreferences_UserId_PreferredCityId_PreferredTrainingTypeId",
                table: "UserPreferences");

            migrationBuilder.AlterColumn<Guid>(
                name: "PreferredTrainingTypeId",
                table: "UserPreferences",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PreferredCityId",
                table: "UserPreferences",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TargetTenantId",
                table: "Recommendations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceId",
                table: "ActivityHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPreferences_UserId_PreferredCityId_PreferredTrainingTypeId",
                table: "UserPreferences",
                columns: new[] { "UserId", "PreferredCityId", "PreferredTrainingTypeId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserPreferences_Weight",
                table: "UserPreferences",
                sql: "[Weight] > 0 AND [Weight] <= 1");

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_UserId_TargetType_TargetId",
                table: "Recommendations",
                columns: new[] { "UserId", "TargetType", "TargetId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Recommendations_Score",
                table: "Recommendations",
                sql: "[Score] >= 0 AND [Score] <= 1");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityHistory_UserId_EventType_SourceId",
                table: "ActivityHistory",
                columns: new[] { "UserId", "EventType", "SourceId" },
                unique: true,
                filter: "[SourceId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserPreferences_UserId_PreferredCityId_PreferredTrainingTypeId",
                table: "UserPreferences");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserPreferences_Weight",
                table: "UserPreferences");

            migrationBuilder.DropIndex(
                name: "IX_Recommendations_UserId_TargetType_TargetId",
                table: "Recommendations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Recommendations_Score",
                table: "Recommendations");

            migrationBuilder.DropIndex(
                name: "IX_ActivityHistory_UserId_EventType_SourceId",
                table: "ActivityHistory");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "ActivityHistory");

            migrationBuilder.AlterColumn<Guid>(
                name: "PreferredTrainingTypeId",
                table: "UserPreferences",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "PreferredCityId",
                table: "UserPreferences",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "TargetTenantId",
                table: "Recommendations",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_UserPreferences_UserId_PreferredCityId_PreferredTrainingTypeId",
                table: "UserPreferences",
                columns: new[] { "UserId", "PreferredCityId", "PreferredTrainingTypeId" },
                unique: true,
                filter: "[PreferredCityId] IS NOT NULL AND [PreferredTrainingTypeId] IS NOT NULL");
        }
    }
}
