using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymLink.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MembershipPaymentMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "MembershipRequests",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "PayInPerson");

            migrationBuilder.Sql(
                """
                UPDATE requestRow
                SET PaymentMethod = CASE
                    WHEN paymentRow.IdempotencyKey LIKE N'manual:%' THEN N'StripeFallback'
                    ELSE N'Stripe'
                END
                FROM MembershipRequests AS requestRow
                INNER JOIN Memberships AS membershipRow
                    ON membershipRow.MembershipRequestId = requestRow.Id
                CROSS APPLY (
                    SELECT TOP (1) candidate.IdempotencyKey
                    FROM Payments AS candidate
                    WHERE candidate.Purpose = N'Membership'
                      AND candidate.TargetId = membershipRow.Id
                    ORDER BY
                        CASE WHEN candidate.Status = N'Succeeded' THEN 0 ELSE 1 END,
                        candidate.CreatedAtUtc DESC,
                        candidate.Id DESC
                ) AS paymentRow;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "MembershipRequests");
        }
    }
}
