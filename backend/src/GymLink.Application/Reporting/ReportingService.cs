using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Identity;
using GymLink.Domain.Trainers;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Reporting;

internal sealed class ReportingService(
    IApplicationDbContext dbContext,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IRequestMetadata requestMetadata,
    IReportPdfRenderer pdfRenderer,
    TimeProvider timeProvider) : IStatisticsService, IReportService
{
    private const int MaximumReportRows = 5000;
    private const string PdfContentType = "application/pdf";

    public async Task<TenantStatisticsSummary> GetTenantSummaryAsync(
        CancellationToken cancellationToken)
    {
        RequireTenant();
        var range = CreateRange();
        var activeMembers = await ActiveMembershipsAt(range.NowUtc)
            .CountAsync(cancellationToken);
        var priorActiveMembers = await ActiveMembershipsAt(range.PreviousMonthEndUtc)
            .CountAsync(cancellationToken);
        var memberChange = CalculateMemberChange(activeMembers, priorActiveMembers);
        var reservationCount = await dbContext.AppointmentReservations.AsNoTracking()
            .CountAsync(
                x => x.StartsAtUtc >= range.StartUtc && x.StartsAtUtc < range.EndUtc,
                cancellationToken);
        var reservationsToday = await dbContext.AppointmentReservations.AsNoTracking()
            .CountAsync(
                x => x.StartsAtUtc >= range.TodayStartUtc &&
                     x.StartsAtUtc < range.TomorrowStartUtc,
                cancellationToken);
        var averageRating = await dbContext.Reviews.AsNoTracking()
            .Select(x => (decimal?)x.Rating)
            .AverageAsync(cancellationToken) ?? 0m;

        return new TenantStatisticsSummary(
            range.Contract,
            activeMembers,
            memberChange,
            reservationCount,
            reservationsToday,
            decimal.Round(averageRating, 2, MidpointRounding.AwayFromZero));
    }

    public async Task<TenantMonthlyStatistics> GetTenantMembersByMonthAsync(
        CancellationToken cancellationToken)
    {
        RequireTenant();
        var range = CreateRange();
        var raw = await dbContext.Memberships.AsNoTracking()
            .Where(x => x.StartsAtUtc >= range.StartUtc && x.StartsAtUtc < range.EndUtc)
            .GroupBy(x => new { x.StartsAtUtc!.Value.Year, x.StartsAtUtc.Value.Month })
            .Select(x => new MonthlyCount(x.Key.Year, x.Key.Month, x.Count()))
            .ToListAsync(cancellationToken);
        return new TenantMonthlyStatistics(range.Contract, FillMonths(range, raw));
    }

    public async Task<MembershipPlanDistribution> GetTenantPlanDistributionAsync(
        CancellationToken cancellationToken)
    {
        RequireTenant();
        var range = CreateRange();
        var groups = await ActiveMembershipsAt(range.NowUtc)
            .GroupBy(x => new { x.MembershipPlanId, x.PlanName })
            .Select(x => new { x.Key.MembershipPlanId, x.Key.PlanName, Count = x.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.PlanName)
            .ToListAsync(cancellationToken);
        var total = groups.Sum(x => x.Count);
        var items = groups.Select(x => new MembershipPlanDistributionItem(
            x.MembershipPlanId,
            x.PlanName,
            x.Count,
            total == 0
                ? 0m
                : decimal.Round(x.Count * 100m / total, 1, MidpointRounding.AwayFromZero)))
            .ToArray();
        return new MembershipPlanDistribution(range.Contract, total, items);
    }

    public async Task<SystemStatisticsSummary> GetSystemSummaryAsync(
        CancellationToken cancellationToken)
    {
        RequireUser();
        var range = CreateRange();
        var rawStatuses = await dbContext.Tenants.AsNoTracking()
            .IgnoreQueryFilters()
            .GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);
        var statuses = rawStatuses
            .OrderBy(x => x.Status)
            .Select(x => new StatusCount(x.Status.ToString(), x.Count))
            .ToArray();
        var result = new SystemStatisticsSummary(
            range.Contract,
            await dbContext.Gyms.AsNoTracking().IgnoreQueryFilters().CountAsync(cancellationToken),
            await dbContext.UserProfiles.AsNoTracking().CountAsync(x => x.IsActive, cancellationToken),
            statuses.Where(x => x.Status == TenantStatus.PendingActivation.ToString())
                .Sum(x => x.Count),
            await dbContext.AppointmentReservations.AsNoTracking().IgnoreQueryFilters()
                .CountAsync(
                    x => x.StartsAtUtc >= range.StartUtc && x.StartsAtUtc < range.EndUtc,
                    cancellationToken),
            statuses);
        await AuditAsync("statistics.system_viewed", null, range, cancellationToken);
        return result;
    }

    public async Task<SystemStatisticsTrends> GetSystemTrendsAsync(
        CancellationToken cancellationToken)
    {
        RequireUser();
        var range = CreateRange();
        var raw = await dbContext.AppointmentReservations.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x => x.StartsAtUtc >= range.StartUtc && x.StartsAtUtc < range.EndUtc)
            .GroupBy(x => new { x.StartsAtUtc.Year, x.StartsAtUtc.Month })
            .Select(x => new MonthlyCount(x.Key.Year, x.Key.Month, x.Count()))
            .ToListAsync(cancellationToken);
        await AuditAsync("statistics.system_viewed", null, range, cancellationToken);
        return new SystemStatisticsTrends(range.Contract, FillMonths(range, raw));
    }

    public async Task<GeneratedReport> GenerateMembershipReportAsync(
        CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var range = CreateRange();
        var rows = await (
            from membership in dbContext.Memberships.AsNoTracking()
            join gym in dbContext.Gyms.AsNoTracking() on membership.GymId equals gym.Id
            join member in dbContext.UserProfiles.AsNoTracking()
                on membership.MemberUserId equals member.Id
            where membership.StartsAtUtc >= range.StartUtc &&
                  membership.StartsAtUtc < range.EndUtc
            orderby membership.StartsAtUtc, member.DisplayName, membership.Id
            select new MembershipReportRow(
                gym.Name,
                member.DisplayName,
                membership.PlanName,
                membership.Status,
                membership.StartsAtUtc!.Value,
                membership.EndsAtUtc))
            .Take(MaximumReportRows + 1)
            .ToListAsync(cancellationToken);
        EnsureReportRowCount(rows.Count);
        var bytes = pdfRenderer.Render(new MembershipReportDocument(range.Contract, rows));
        await AuditAsync("report.memberships_generated", tenantId, range, cancellationToken);
        return new GeneratedReport(
            bytes,
            $"gymlink-clanstva-{range.Contract.WindowEnd:yyyy-MM-dd}.pdf",
            PdfContentType,
            rows.Count);
    }

    public async Task<GeneratedReport> GenerateReservationReportAsync(
        CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var range = CreateRange();
        var rows = await (
            from reservation in dbContext.AppointmentReservations.AsNoTracking()
            join membership in dbContext.Memberships.AsNoTracking()
                on reservation.MembershipId equals membership.Id
            join gym in dbContext.Gyms.AsNoTracking() on membership.GymId equals gym.Id
            join member in dbContext.UserProfiles.AsNoTracking()
                on reservation.MemberUserId equals member.Id
            join trainer in dbContext.TrainerProfiles.AsNoTracking()
                on reservation.TrainerProfileId equals trainer.Id
            join trainerUser in dbContext.UserProfiles.AsNoTracking()
                on trainer.UserId equals trainerUser.Id
            join offering in dbContext.TrainerServiceOfferings.AsNoTracking()
                on reservation.TrainerServiceOfferingId equals offering.Id
            join payment in dbContext.Payments.AsNoTracking()
                on reservation.PaymentId equals payment.Id into payments
            from payment in payments.DefaultIfEmpty()
            where reservation.StartsAtUtc >= range.StartUtc &&
                  reservation.StartsAtUtc < range.EndUtc
            orderby reservation.StartsAtUtc, member.DisplayName, reservation.Id
            select new ReservationReportRow(
                gym.Name,
                member.DisplayName,
                trainerUser.DisplayName,
                offering.Name,
                reservation.StartsAtUtc,
                reservation.Status,
                payment == null ? null : payment.Status))
            .Take(MaximumReportRows + 1)
            .ToListAsync(cancellationToken);
        EnsureReportRowCount(rows.Count);
        var bytes = pdfRenderer.Render(new ReservationReportDocument(range.Contract, rows));
        await AuditAsync("report.reservations_generated", tenantId, range, cancellationToken);
        return new GeneratedReport(
            bytes,
            $"gymlink-rezervacije-{range.Contract.WindowEnd:yyyy-MM-dd}.pdf",
            PdfContentType,
            rows.Count);
    }

    private IQueryable<GymLink.Domain.Memberships.Membership> ActiveMembershipsAt(
        DateTime instantUtc) =>
        dbContext.Memberships.AsNoTracking().Where(x =>
            x.StartsAtUtc.HasValue && x.StartsAtUtc <= instantUtc &&
            x.EndsAtUtc.HasValue && x.EndsAtUtc > instantUtc &&
            (x.Status == MembershipStatus.Active ||
             (x.Status != MembershipStatus.PendingPayment &&
              (!x.StatusChangedAtUtc.HasValue || x.StatusChangedAtUtc > instantUtc))));

    private async Task AuditAsync(
        string action,
        Guid? tenantId,
        ReportRange range,
        CancellationToken cancellationToken)
    {
        dbContext.SecurityAuditRecords.Add(new SecurityAuditRecord
        {
            ActorUserId = RequireUser(),
            TargetTenantId = tenantId,
            Action = action,
            TargetType = "Reporting",
            Reason = $"{range.Contract.WindowStart:yyyy-MM-dd}:{range.Contract.WindowEnd:yyyy-MM-dd}",
            CorrelationId = requestMetadata.CorrelationId,
            OccurredAtUtc = range.NowUtc,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Guid RequireTenant() => tenantContext.TenantId is { } tenantId && tenantId != Guid.Empty
        ? tenantId
        : throw new AuthorizationDeniedException(
            "tenant_context_required",
            "An active tenant assignment is required.");

    private Guid RequireUser() => currentUser.UserId is { } userId && userId != Guid.Empty
        ? userId
        : throw new AuthorizationDeniedException();

    internal static void EnsureReportRowCount(int count)
    {
        if (count > MaximumReportRows)
        {
            throw new ApplicationRuleException(
                "report_too_large",
                "The report exceeds 5,000 rows.");
        }
    }

    internal static decimal CalculateMemberChange(int currentCount, int previousCount) =>
        previousCount == 0
            ? currentCount == 0 ? 0m : 100m
            : decimal.Round(
                (currentCount - previousCount) * 100m / previousCount,
                1,
                MidpointRounding.AwayFromZero);

    private ReportRange CreateRange()
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(
            TrainerAvailabilitySchedule.SarajevoTimeZoneId);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone);
        var currentMonth = new DateOnly(localNow.Year, localNow.Month, 1);
        var windowStart = currentMonth.AddMonths(-5);
        var windowEndExclusive = currentMonth.AddMonths(1);
        var today = DateOnly.FromDateTime(localNow);
        var startUtc = ToUtc(windowStart, timeZone);
        var endUtc = ToUtc(windowEndExclusive, timeZone);
        var todayStartUtc = ToUtc(today, timeZone);
        var tomorrowStartUtc = ToUtc(today.AddDays(1), timeZone);
        var previousMonthEndUtc = ToUtc(currentMonth, timeZone).AddTicks(-1);
        return new ReportRange(
            nowUtc,
            startUtc,
            endUtc,
            todayStartUtc,
            tomorrowStartUtc,
            previousMonthEndUtc,
            new ReportingWindow(
                windowStart,
                windowEndExclusive.AddDays(-1),
                TrainerAvailabilitySchedule.SarajevoTimeZoneId,
                nowUtc));
    }

    internal static DateTime ToUtc(DateOnly date, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified),
            timeZone);

    private static MonthlyCount[] FillMonths(
        ReportRange range,
        IReadOnlyCollection<MonthlyCount> raw)
    {
        var lookup = raw.ToDictionary(x => (x.Year, x.Month), x => x.Count);
        return Enumerable.Range(0, 6)
            .Select(offset => range.Contract.WindowStart.AddMonths(offset))
            .Select(month => new MonthlyCount(
                month.Year,
                month.Month,
                lookup.GetValueOrDefault((month.Year, month.Month))))
            .ToArray();
    }

    private sealed record ReportRange(
        DateTime NowUtc,
        DateTime StartUtc,
        DateTime EndUtc,
        DateTime TodayStartUtc,
        DateTime TomorrowStartUtc,
        DateTime PreviousMonthEndUtc,
        ReportingWindow Contract);
}
