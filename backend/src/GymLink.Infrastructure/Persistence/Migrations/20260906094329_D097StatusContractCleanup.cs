using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymLink.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D097StatusContractCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE [GymRegistrationRequests]
                SET [Status] = N'Rejected',
                    [DecisionReason] = CASE
                        WHEN NULLIF(LTRIM(RTRIM([DecisionReason])), N'') IS NULL
                            THEN N'Legacy draft retired during status cleanup.'
                        ELSE LEFT([DecisionReason] + N' | Legacy draft retired during status cleanup.', 1000)
                    END,
                    [DecidedAtUtc] = COALESCE(
                        [DecidedAtUtc], [SubmittedAtUtc], [UpdatedAtUtc], [CreatedAtUtc])
                WHERE [Status] = N'Draft';

                UPDATE [UserGymAssignments]
                SET [Status] = N'Ended',
                    [EndsAtUtc] = COALESCE([EndsAtUtc], [UpdatedAtUtc], [CreatedAtUtc]),
                    [Reason] = CASE
                        WHEN NULLIF(LTRIM(RTRIM([Reason])), N'') IS NULL
                            THEN N'Legacy inactive assignment retired during status cleanup.'
                        ELSE LEFT([Reason] + N' | Legacy inactive assignment retired during status cleanup.', 1000)
                    END
                WHERE [Status] IN (N'Invited', N'Suspended');

                UPDATE [MembershipRequests]
                SET [PaymentMethod] = N'Stripe'
                WHERE [PaymentMethod] = N'StripeFallback';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The normalized values remain valid for the previous application version.
        }
    }
}
