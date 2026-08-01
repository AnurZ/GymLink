using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymLink.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase93TrainerProfileImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageContentType",
                table: "TrainerProfiles",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ImageFileSizeBytes",
                table: "TrainerProfiles",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageStorageKey",
                table: "TrainerProfiles",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "TrainerProfiles",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrainerProfiles_ImageContentType",
                table: "TrainerProfiles",
                sql: "[ImageContentType] IS NULL OR [ImageContentType] IN ('image/jpeg', 'image/png', 'image/webp')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrainerProfiles_ImageFileSize",
                table: "TrainerProfiles",
                sql: "[ImageFileSizeBytes] IS NULL OR ([ImageFileSizeBytes] > 0 AND [ImageFileSizeBytes] <= 5242880)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrainerProfiles_ImageMetadata",
                table: "TrainerProfiles",
                sql: "([ImageStorageKey] IS NULL AND [ImageUrl] IS NULL AND [ImageContentType] IS NULL AND [ImageFileSizeBytes] IS NULL) OR ([ImageStorageKey] IS NOT NULL AND [ImageUrl] IS NOT NULL AND [ImageContentType] IS NOT NULL AND [ImageFileSizeBytes] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TrainerProfiles_ImageContentType",
                table: "TrainerProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TrainerProfiles_ImageFileSize",
                table: "TrainerProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TrainerProfiles_ImageMetadata",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "ImageContentType",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "ImageFileSizeBytes",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "ImageStorageKey",
                table: "TrainerProfiles");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "TrainerProfiles");
        }
    }
}
