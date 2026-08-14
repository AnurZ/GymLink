using GymLink.Domain.Enums;

namespace GymLink.Application.Reporting;

public sealed record ReportingWindow(
    DateOnly WindowStart,
    DateOnly WindowEnd,
    string TimeZone,
    DateTime GeneratedAtUtc);

public sealed record TenantStatisticsSummary(
    ReportingWindow Window,
    int ActiveMemberCount,
    decimal MemberChangePercentage,
    int ReservationCount,
    int ReservationsToday,
    decimal AverageTrainerRating);

public sealed record MonthlyCount(int Year, int Month, int Count);

public sealed record TenantMonthlyStatistics(
    ReportingWindow Window,
    IReadOnlyList<MonthlyCount> Items);

public sealed record MembershipPlanDistributionItem(
    Guid MembershipPlanId,
    string PlanName,
    int Count,
    decimal Percentage);

public sealed record MembershipPlanDistribution(
    ReportingWindow Window,
    int Total,
    IReadOnlyList<MembershipPlanDistributionItem> Items);

public sealed record StatusCount(string Status, int Count);

public sealed record SystemStatisticsSummary(
    ReportingWindow Window,
    int TotalGyms,
    int ActiveUsers,
    int PendingActivationGyms,
    int ReservationCount,
    IReadOnlyList<StatusCount> GymStatusDistribution);

public sealed record SystemStatisticsTrends(
    ReportingWindow Window,
    IReadOnlyList<MonthlyCount> ReservationsByMonth);

public sealed record MembershipReportRow(
    string GymName,
    string MemberName,
    string PlanName,
    MembershipStatus Status,
    DateTime StartsAtUtc,
    DateTime? EndsAtUtc);

public sealed record ReservationReportRow(
    string GymName,
    string MemberName,
    string TrainerName,
    string OfferingName,
    DateTime StartsAtUtc,
    ReservationStatus Status,
    ReservationPaymentMethod PaymentMethod);

public sealed record MembershipReportDocument(
    ReportingWindow Window,
    IReadOnlyList<MembershipReportRow> Rows);

public sealed record ReservationReportDocument(
    ReportingWindow Window,
    IReadOnlyList<ReservationReportRow> Rows);

public sealed record GeneratedReport(
    byte[] Content,
    string FileName,
    string ContentType,
    int RecordCount);

public interface IReportPdfRenderer
{
    byte[] Render(MembershipReportDocument document);
    byte[] Render(ReservationReportDocument document);
}

public interface IStatisticsService
{
    Task<TenantStatisticsSummary> GetTenantSummaryAsync(CancellationToken cancellationToken);
    Task<TenantMonthlyStatistics> GetTenantMembersByMonthAsync(CancellationToken cancellationToken);
    Task<MembershipPlanDistribution> GetTenantPlanDistributionAsync(CancellationToken cancellationToken);
    Task<SystemStatisticsSummary> GetSystemSummaryAsync(CancellationToken cancellationToken);
    Task<SystemStatisticsTrends> GetSystemTrendsAsync(CancellationToken cancellationToken);
}

public interface IReportService
{
    Task<GeneratedReport> GenerateMembershipReportAsync(CancellationToken cancellationToken);
    Task<GeneratedReport> GenerateReservationReportAsync(CancellationToken cancellationToken);
}
