using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF migration operations require inline column arrays.

namespace GymLink.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase9MemberTrainerChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_ConversationId_SentAtUtc",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_ConversationParticipants_UserId",
                table: "ConversationParticipants");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientMessageId",
                table: "Messages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastMessageAtUtc",
                table: "Conversations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MemberUserId",
                table: "Conversations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TrainerUserId",
                table: "Conversations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReadAtUtc",
                table: "ConversationParticipants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE m
                SET m.ClientMessageId = m.Id
                FROM Messages AS m;

                UPDATE c
                SET
                    c.MemberUserId = r.MemberUserId,
                    c.TrainerUserId = tp.UserId
                FROM Conversations AS c
                INNER JOIN AppointmentReservations AS r ON r.Id = c.ReservationId
                INNER JOIN TrainerProfiles AS tp ON tp.Id = r.TrainerProfileId;

                UPDATE c
                SET c.LastMessageAtUtc = latest.LastMessageAtUtc
                FROM Conversations AS c
                INNER JOIN (
                    SELECT ConversationId, MAX(SentAtUtc) AS LastMessageAtUtc
                    FROM Messages
                    GROUP BY ConversationId
                ) AS latest ON latest.ConversationId = c.Id;

                IF EXISTS (
                    SELECT 1
                    FROM Conversations
                    WHERE MemberUserId IS NULL OR TrainerUserId IS NULL
                )
                    THROW 51000, 'Existing conversations require reservation-backed Member and Trainer participants before Phase 9 migration.', 1;

                IF EXISTS (SELECT 1 FROM Messages WHERE LEN([Text]) > 2000)
                    THROW 51000, 'Existing chat messages exceed the Phase 9 2000-character limit.', 1;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "ClientMessageId",
                table: "Messages",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Text",
                table: "Messages",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(4000)",
                oldMaxLength: 4000);

            migrationBuilder.AlterColumn<Guid>(
                name: "MemberUserId",
                table: "Conversations",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TrainerUserId",
                table: "Conversations",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_SenderUserId_ClientMessageId",
                table: "Messages",
                columns: new[] { "ConversationId", "SenderUserId", "ClientMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_SentAtUtc_Id",
                table: "Messages",
                columns: new[] { "ConversationId", "SentAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_MemberUserId",
                table: "Conversations",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_TenantId_LastMessageAtUtc",
                table: "Conversations",
                columns: new[] { "TenantId", "LastMessageAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_TenantId_MemberUserId_TrainerUserId",
                table: "Conversations",
                columns: new[] { "TenantId", "MemberUserId", "TrainerUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_TrainerUserId",
                table: "Conversations",
                column: "TrainerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationParticipants_UserId_LeftAtUtc",
                table: "ConversationParticipants",
                columns: new[] { "UserId", "LeftAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_UserProfiles_MemberUserId",
                table: "Conversations",
                column: "MemberUserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_UserProfiles_TrainerUserId",
                table: "Conversations",
                column: "TrainerUserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_UserProfiles_MemberUserId",
                table: "Conversations");

            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_UserProfiles_TrainerUserId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ConversationId_SenderUserId_ClientMessageId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ConversationId_SentAtUtc_Id",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_MemberUserId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_TenantId_LastMessageAtUtc",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_TenantId_MemberUserId_TrainerUserId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_TrainerUserId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_ConversationParticipants_UserId_LeftAtUtc",
                table: "ConversationParticipants");

            migrationBuilder.DropColumn(
                name: "ClientMessageId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "LastMessageAtUtc",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "MemberUserId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "TrainerUserId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "LastReadAtUtc",
                table: "ConversationParticipants");

            migrationBuilder.AlterColumn<string>(
                name: "Text",
                table: "Messages",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_SentAtUtc",
                table: "Messages",
                columns: new[] { "ConversationId", "SentAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationParticipants_UserId",
                table: "ConversationParticipants",
                column: "UserId");
        }
    }
}
