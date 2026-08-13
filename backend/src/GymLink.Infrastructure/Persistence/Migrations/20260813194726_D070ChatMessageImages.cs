using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymLink.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D070ChatMessageImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageContentType",
                table: "Messages",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ImageFileSizeBytes",
                table: "Messages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageStorageKey",
                table: "Messages",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Messages_ImageContentType",
                table: "Messages",
                sql: "[ImageContentType] IS NULL OR [ImageContentType] IN ('image/jpeg', 'image/png', 'image/webp')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Messages_ImageFileSize",
                table: "Messages",
                sql: "[ImageFileSizeBytes] IS NULL OR ([ImageFileSizeBytes] > 0 AND [ImageFileSizeBytes] <= 5242880)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Messages_ImageMetadata",
                table: "Messages",
                sql: "([ImageStorageKey] IS NULL AND [ImageContentType] IS NULL AND [ImageFileSizeBytes] IS NULL) OR ([ImageStorageKey] IS NOT NULL AND [ImageContentType] IS NOT NULL AND [ImageFileSizeBytes] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Messages_ImageContentType",
                table: "Messages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Messages_ImageFileSize",
                table: "Messages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Messages_ImageMetadata",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ImageContentType",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ImageFileSizeBytes",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ImageStorageKey",
                table: "Messages");
        }
    }
}
