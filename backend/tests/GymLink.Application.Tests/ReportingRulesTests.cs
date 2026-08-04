using GymLink.Application.Common;
using GymLink.Application.Reporting;

namespace GymLink.Application.Tests;

public sealed class ReportingRulesTests
{
    [Theory]
    [InlineData(2026, 3, 1, "2026-02-28T23:00:00Z")]
    [InlineData(2026, 8, 1, "2026-07-31T22:00:00Z")]
    public void Sarajevo_month_boundaries_convert_to_utc_across_dst(
        int year,
        int month,
        int day,
        string expectedUtc)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Sarajevo");

        var result = ReportingService.ToUtc(new DateOnly(year, month, day), timeZone);

        Assert.Equal(DateTime.Parse(
            expectedUtc,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal), result);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(2, 0, 100)]
    [InlineData(3, 2, 50)]
    [InlineData(2, 3, -33.3)]
    public void Member_change_uses_previous_month_end_baseline(
        int current,
        int previous,
        decimal expected) =>
        Assert.Equal(expected, ReportingService.CalculateMemberChange(current, previous));

    [Fact]
    public void Report_row_limit_accepts_5000_and_rejects_5001()
    {
        ReportingService.EnsureReportRowCount(5000);

        var exception = Assert.Throws<ApplicationRuleException>(
            () => ReportingService.EnsureReportRowCount(5001));

        Assert.Equal("report_too_large", exception.Code);
    }
}
