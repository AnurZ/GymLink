using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymLink.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3IdentityTenancy : Migration
    {
        private static readonly string[] ActorOccurredColumns =
            ["ActorUserId", "OccurredAtUtc"];

        private static readonly string[] TenantOccurredColumns =
            ["TargetTenantId", "OccurredAtUtc"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActivityHistory_Users_UserId",
                table: "ActivityHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_AppointmentReservations_Users_CancelledByUserId",
                table: "AppointmentReservations");

            migrationBuilder.DropForeignKey(
                name: "FK_AppointmentReservations_Users_MemberUserId",
                table: "AppointmentReservations");

            migrationBuilder.DropForeignKey(
                name: "FK_ConversationParticipants_Users_UserId",
                table: "ConversationParticipants");

            migrationBuilder.DropForeignKey(
                name: "FK_GymRegistrationRequests_Users_ApplicantUserId",
                table: "GymRegistrationRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_GymRegistrationRequests_Users_DecidedByUserId",
                table: "GymRegistrationRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_MembershipRequests_Users_DecidedByUserId",
                table: "MembershipRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_MembershipRequests_Users_MemberUserId",
                table: "MembershipRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Memberships_Users_MemberUserId",
                table: "Memberships");

            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Users_SenderUserId",
                table: "Messages");

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Users_RecipientUserId",
                table: "Notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Users_UserId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Recommendations_Users_UserId",
                table: "Recommendations");

            migrationBuilder.DropForeignKey(
                name: "FK_RefreshTokenSessions_Users_UserId",
                table: "RefreshTokenSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_Refunds_Users_RequestedByUserId",
                table: "Refunds");

            migrationBuilder.DropForeignKey(
                name: "FK_Reviews_Users_ReviewerUserId",
                table: "Reviews");

            migrationBuilder.DropForeignKey(
                name: "FK_Tenants_Users_StatusChangedByUserId",
                table: "Tenants");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainerProfiles_Users_UserId",
                table: "TrainerProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_UserGymAssignments_Users_ApprovedByUserId",
                table: "UserGymAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_UserGymAssignments_Users_UserId",
                table: "UserGymAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPreferences_Users_UserId",
                table: "UserPreferences");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_UserGymAssignments_UserId",
                table: "UserGymAssignments");

            migrationBuilder.AddColumn<string>(
                name: "RevocationReason",
                table: "RefreshTokenSessions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "RefreshTokenSessions",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: Array.Empty<byte>());

            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "GymRegistrationRequests",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "GymRegistrationRequests",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "GymRegistrationRequests",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProposedDescription",
                table: "GymRegistrationRequests",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "IdentityRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdentityUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdentityRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdentityRoleClaims_IdentityRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "IdentityRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IdentityUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdentityUserClaims_IdentityUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "IdentityUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IdentityUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_IdentityUserLogins_IdentityUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "IdentityUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IdentityUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_IdentityUserRoles_IdentityRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "IdentityRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IdentityUserRoles_IdentityUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "IdentityUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IdentityUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_IdentityUserTokens_IdentityUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "IdentityUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ImageStorageKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    TokenVersion = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserProfiles_IdentityUsers_Id",
                        column: x => x.Id,
                        principalTable: "IdentityUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SecurityAuditRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TargetTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityAuditRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityAuditRecords_Tenants_TargetTenantId",
                        column: x => x.TargetTenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SecurityAuditRecords_UserProfiles_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SecurityAuditRecords_UserProfiles_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserGymAssignments_UserId",
                table: "UserGymAssignments",
                column: "UserId",
                unique: true,
                filter: "[Status] = 'Active' AND [Role] IN ('GymAdmin', 'Trainer')");

            migrationBuilder.CreateIndex(
                name: "IX_GymRegistrationRequests_ApplicantUserId",
                table: "GymRegistrationRequests",
                column: "ApplicantUserId",
                unique: true,
                filter: "[Status] = 'Submitted'");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityRoleClaims_RoleId",
                table: "IdentityRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "IdentityRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityUserClaims_UserId",
                table: "IdentityUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityUserLogins_UserId",
                table: "IdentityUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityUserRoles_RoleId",
                table: "IdentityUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "IdentityUsers",
                column: "NormalizedEmail",
                unique: true,
                filter: "[NormalizedEmail] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "IdentityUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditRecords_ActorUserId_OccurredAtUtc",
                table: "SecurityAuditRecords",
                columns: ActorOccurredColumns);

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditRecords_TargetTenantId_OccurredAtUtc",
                table: "SecurityAuditRecords",
                columns: TenantOccurredColumns);

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditRecords_TargetUserId",
                table: "SecurityAuditRecords",
                column: "TargetUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ActivityHistory_UserProfiles_UserId",
                table: "ActivityHistory",
                column: "UserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppointmentReservations_UserProfiles_CancelledByUserId",
                table: "AppointmentReservations",
                column: "CancelledByUserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppointmentReservations_UserProfiles_MemberUserId",
                table: "AppointmentReservations",
                column: "MemberUserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ConversationParticipants_UserProfiles_UserId",
                table: "ConversationParticipants",
                column: "UserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GymRegistrationRequests_UserProfiles_ApplicantUserId",
                table: "GymRegistrationRequests",
                column: "ApplicantUserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GymRegistrationRequests_UserProfiles_DecidedByUserId",
                table: "GymRegistrationRequests",
                column: "DecidedByUserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipRequests_UserProfiles_DecidedByUserId",
                table: "MembershipRequests",
                column: "DecidedByUserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipRequests_UserProfiles_MemberUserId",
                table: "MembershipRequests",
                column: "MemberUserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Memberships_UserProfiles_MemberUserId",
                table: "Memberships",
                column: "MemberUserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_UserProfiles_SenderUserId",
                table: "Messages",
                column: "SenderUserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_UserProfiles_RecipientUserId",
                table: "Notifications",
                column: "RecipientUserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_UserProfiles_UserId",
                table: "Payments",
                column: "UserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Recommendations_UserProfiles_UserId",
                table: "Recommendations",
                column: "UserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshTokenSessions_UserProfiles_UserId",
                table: "RefreshTokenSessions",
                column: "UserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Refunds_UserProfiles_RequestedByUserId",
                table: "Refunds",
                column: "RequestedByUserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reviews_UserProfiles_ReviewerUserId",
                table: "Reviews",
                column: "ReviewerUserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_UserProfiles_StatusChangedByUserId",
                table: "Tenants",
                column: "StatusChangedByUserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainerProfiles_UserProfiles_UserId",
                table: "TrainerProfiles",
                column: "UserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserGymAssignments_UserProfiles_ApprovedByUserId",
                table: "UserGymAssignments",
                column: "ApprovedByUserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserGymAssignments_UserProfiles_UserId",
                table: "UserGymAssignments",
                column: "UserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPreferences_UserProfiles_UserId",
                table: "UserPreferences",
                column: "UserId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActivityHistory_UserProfiles_UserId",
                table: "ActivityHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_AppointmentReservations_UserProfiles_CancelledByUserId",
                table: "AppointmentReservations");

            migrationBuilder.DropForeignKey(
                name: "FK_AppointmentReservations_UserProfiles_MemberUserId",
                table: "AppointmentReservations");

            migrationBuilder.DropForeignKey(
                name: "FK_ConversationParticipants_UserProfiles_UserId",
                table: "ConversationParticipants");

            migrationBuilder.DropForeignKey(
                name: "FK_GymRegistrationRequests_UserProfiles_ApplicantUserId",
                table: "GymRegistrationRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_GymRegistrationRequests_UserProfiles_DecidedByUserId",
                table: "GymRegistrationRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_MembershipRequests_UserProfiles_DecidedByUserId",
                table: "MembershipRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_MembershipRequests_UserProfiles_MemberUserId",
                table: "MembershipRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Memberships_UserProfiles_MemberUserId",
                table: "Memberships");

            migrationBuilder.DropForeignKey(
                name: "FK_Messages_UserProfiles_SenderUserId",
                table: "Messages");

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_UserProfiles_RecipientUserId",
                table: "Notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_UserProfiles_UserId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Recommendations_UserProfiles_UserId",
                table: "Recommendations");

            migrationBuilder.DropForeignKey(
                name: "FK_RefreshTokenSessions_UserProfiles_UserId",
                table: "RefreshTokenSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_Refunds_UserProfiles_RequestedByUserId",
                table: "Refunds");

            migrationBuilder.DropForeignKey(
                name: "FK_Reviews_UserProfiles_ReviewerUserId",
                table: "Reviews");

            migrationBuilder.DropForeignKey(
                name: "FK_Tenants_UserProfiles_StatusChangedByUserId",
                table: "Tenants");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainerProfiles_UserProfiles_UserId",
                table: "TrainerProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_UserGymAssignments_UserProfiles_ApprovedByUserId",
                table: "UserGymAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_UserGymAssignments_UserProfiles_UserId",
                table: "UserGymAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPreferences_UserProfiles_UserId",
                table: "UserPreferences");

            migrationBuilder.DropTable(
                name: "IdentityRoleClaims");

            migrationBuilder.DropTable(
                name: "IdentityUserClaims");

            migrationBuilder.DropTable(
                name: "IdentityUserLogins");

            migrationBuilder.DropTable(
                name: "IdentityUserRoles");

            migrationBuilder.DropTable(
                name: "IdentityUserTokens");

            migrationBuilder.DropTable(
                name: "SecurityAuditRecords");

            migrationBuilder.DropTable(
                name: "IdentityRoles");

            migrationBuilder.DropTable(
                name: "UserProfiles");

            migrationBuilder.DropTable(
                name: "IdentityUsers");

            migrationBuilder.DropIndex(
                name: "IX_UserGymAssignments_UserId",
                table: "UserGymAssignments");

            migrationBuilder.DropIndex(
                name: "IX_GymRegistrationRequests_ApplicantUserId",
                table: "GymRegistrationRequests");

            migrationBuilder.DropColumn(
                name: "RevocationReason",
                table: "RefreshTokenSessions");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "RefreshTokenSessions");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "GymRegistrationRequests");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "GymRegistrationRequests");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "GymRegistrationRequests");

            migrationBuilder.DropColumn(
                name: "ProposedDescription",
                table: "GymRegistrationRequests");

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    ImageStorageKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TokenVersion = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserGymAssignments_UserId",
                table: "UserGymAssignments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                table: "Users",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ActivityHistory_Users_UserId",
                table: "ActivityHistory",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppointmentReservations_Users_CancelledByUserId",
                table: "AppointmentReservations",
                column: "CancelledByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppointmentReservations_Users_MemberUserId",
                table: "AppointmentReservations",
                column: "MemberUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ConversationParticipants_Users_UserId",
                table: "ConversationParticipants",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GymRegistrationRequests_Users_ApplicantUserId",
                table: "GymRegistrationRequests",
                column: "ApplicantUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GymRegistrationRequests_Users_DecidedByUserId",
                table: "GymRegistrationRequests",
                column: "DecidedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipRequests_Users_DecidedByUserId",
                table: "MembershipRequests",
                column: "DecidedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipRequests_Users_MemberUserId",
                table: "MembershipRequests",
                column: "MemberUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Memberships_Users_MemberUserId",
                table: "Memberships",
                column: "MemberUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Users_SenderUserId",
                table: "Messages",
                column: "SenderUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Users_RecipientUserId",
                table: "Notifications",
                column: "RecipientUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Users_UserId",
                table: "Payments",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Recommendations_Users_UserId",
                table: "Recommendations",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshTokenSessions_Users_UserId",
                table: "RefreshTokenSessions",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Refunds_Users_RequestedByUserId",
                table: "Refunds",
                column: "RequestedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reviews_Users_ReviewerUserId",
                table: "Reviews",
                column: "ReviewerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_Users_StatusChangedByUserId",
                table: "Tenants",
                column: "StatusChangedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainerProfiles_Users_UserId",
                table: "TrainerProfiles",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserGymAssignments_Users_ApprovedByUserId",
                table: "UserGymAssignments",
                column: "ApprovedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserGymAssignments_Users_UserId",
                table: "UserGymAssignments",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPreferences_Users_UserId",
                table: "UserPreferences",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
