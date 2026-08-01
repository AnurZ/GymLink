using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymLink.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase94GymImageGalleryMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "GymImages",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "GymImages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "GymImages",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: Array.Empty<byte>());

            migrationBuilder.AddCheckConstraint(
                name: "CK_GymImages_ContentType",
                table: "GymImages",
                sql: "[ContentType] IS NULL OR [ContentType] IN ('image/jpeg', 'image/png', 'image/webp')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GymImages_FileSize",
                table: "GymImages",
                sql: "[FileSizeBytes] IS NULL OR ([FileSizeBytes] > 0 AND [FileSizeBytes] <= 5242880)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GymImages_LocalMetadata",
                table: "GymImages",
                sql: "([ContentType] IS NULL AND [FileSizeBytes] IS NULL) OR ([ContentType] IS NOT NULL AND [FileSizeBytes] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GymImages_SortOrder",
                table: "GymImages",
                sql: "[SortOrder] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_GymImages_ContentType",
                table: "GymImages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GymImages_FileSize",
                table: "GymImages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GymImages_LocalMetadata",
                table: "GymImages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GymImages_SortOrder",
                table: "GymImages");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "GymImages");

            migrationBuilder.DropColumn(
                name: "FileSizeBytes",
                table: "GymImages");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "GymImages");
        }
    }
}
