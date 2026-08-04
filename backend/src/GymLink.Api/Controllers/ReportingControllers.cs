using GymLink.Application.Authorization;
using GymLink.Application.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Authorize(Policy = PolicyNames.TenantGymAdmin)]
[Route("api/tenant/statistics")]
public sealed class TenantStatisticsController(IStatisticsService service) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken) =>
        Ok(await service.GetTenantSummaryAsync(cancellationToken));

    [HttpGet("members-by-month")]
    public async Task<IActionResult> MembersByMonth(CancellationToken cancellationToken) =>
        Ok(await service.GetTenantMembersByMonthAsync(cancellationToken));

    [HttpGet("membership-plan-distribution")]
    public async Task<IActionResult> MembershipPlanDistribution(
        CancellationToken cancellationToken) =>
        Ok(await service.GetTenantPlanDistributionAsync(cancellationToken));
}

[ApiController]
[Authorize(Policy = PolicyNames.CentralAdminOnly)]
[Route("api/admin/statistics")]
public sealed class AdminStatisticsController(IStatisticsService service) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken) =>
        Ok(await service.GetSystemSummaryAsync(cancellationToken));

    [HttpGet("trends")]
    public async Task<IActionResult> Trends(CancellationToken cancellationToken) =>
        Ok(await service.GetSystemTrendsAsync(cancellationToken));
}

[ApiController]
[Authorize(Policy = PolicyNames.TenantGymAdmin)]
[Route("api/tenant/reports")]
public sealed class TenantReportsController(IReportService service) : ControllerBase
{
    [HttpGet("memberships.pdf")]
    public async Task<IActionResult> Memberships(CancellationToken cancellationToken) =>
        Report(await service.GenerateMembershipReportAsync(cancellationToken));

    [HttpGet("reservations.pdf")]
    public async Task<IActionResult> Reservations(CancellationToken cancellationToken) =>
        Report(await service.GenerateReservationReportAsync(cancellationToken));

    private FileContentResult Report(GeneratedReport report)
    {
        Response.Headers["X-Report-Record-Count"] = report.RecordCount.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        return File(report.Content, report.ContentType, report.FileName);
    }
}
