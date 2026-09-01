using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GymLink.Application.Identity;
using GymLink.Application.Reporting;
using GymLink.Domain.Enums;
using GymLink.Domain.Memberships;
using GymLink.Domain.Reservations;
using GymLink.Infrastructure.Persistence;
using GymLink.Infrastructure.Reporting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GymLink.IntegrationTests;

public sealed class Phase11ReportingApiTests
{
    private const string Password = "Test123!";
    private const string SigningKey = "integration-test-signing-key-at-least-32-bytes";
    private static readonly DateTimeOffset ReportingNow =
        new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly System.Text.Json.JsonSerializerOptions WebJsonOptions = new(
        System.Text.Json.JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(ReservationPaymentMethod.Stripe, "Online")]
    [InlineData(ReservationPaymentMethod.PayInPerson, "Uživo")]
    public void Reservation_report_uses_payment_method_labels(
        ReservationPaymentMethod paymentMethod,
        string expected) =>
        Assert.Equal(
            expected,
            QuestPdfReportRenderer.ReservationPaymentMethodLabel(paymentMethod));

    [Fact]
    public async Task Statistics_and_reports_are_bounded_tenant_scoped_authorized_and_audited()
    {
        var connectionString = TestSqlServer.ConnectionString(
            $"GymLink_Phase11_{Guid.NewGuid():N}");
        try
        {
            await using (var migration = CreateContext(connectionString))
            {
                await migration.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
            var emptyWindow = new ReportingWindow(
                new DateOnly(2026, 3, 1),
                new DateOnly(2026, 8, 31),
                "Europe/Sarajevo",
                DateTime.UtcNow);
            var renderer = factory.Services.GetRequiredService<IReportPdfRenderer>();
            Assert.Equal(
                "%PDF"u8.ToArray(),
                renderer.Render(new MembershipReportDocument(emptyWindow, []))[..4]);
            Assert.Equal(
                "%PDF"u8.ToArray(),
                renderer.Render(new ReservationReportDocument(emptyWindow, []))[..4]);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await client.GetAsync("/api/tenant/statistics/summary")).StatusCode);

            var member = await LoginAsync(client, "mobile1");
            Authorize(client, member);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await client.GetAsync("/api/tenant/statistics/summary")).StatusCode);

            var arenaAdmin = await LoginAsync(client, "admin.arena");
            Authorize(client, arenaAdmin);
            await ApplyMonthBoundaryFixtureAsync(
                connectionString,
                arenaAdmin.User.Tenant!.Id);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await client.GetAsync("/api/admin/statistics/summary")).StatusCode);
            var summaryResponse = await client.GetAsync("/api/tenant/statistics/summary");
            Assert.True(
                summaryResponse.IsSuccessStatusCode,
                await summaryResponse.Content.ReadAsStringAsync());
            var summaryJson = await summaryResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("memberChangePercentage", summaryJson);
            var summary = System.Text.Json.JsonSerializer.Deserialize<TenantStatisticsSummary>(
                summaryJson,
                WebJsonOptions);
            Assert.NotNull(summary);
            Assert.Equal(new DateOnly(2026, 3, 1), summary.Window.WindowStart);
            Assert.Equal(new DateOnly(2026, 8, 31), summary.Window.WindowEnd);
            Assert.Equal("Europe/Sarajevo", summary.Window.TimeZone);
            Assert.Equal(ReportingNow.UtcDateTime, summary.Window.GeneratedAtUtc);
            Assert.Equal(2, summary.ActiveMemberCount);
            Assert.Equal(2, summary.MembershipPeriodCount);
            Assert.Equal(1, summary.PreviousMonthEndMembershipPeriodCount);
            Assert.Equal(100m, summary.MembershipPeriodChangePercentage);
            Assert.Equal(8, summary.ReservationCount);

            var months = await client.GetFromJsonAsync<TenantMonthlyStatistics>(
                "/api/tenant/statistics/members-by-month");
            Assert.NotNull(months);
            Assert.Equal(6, months.Items.Count);
            Assert.Equal([3, 4, 5, 6, 7, 8], months.Items.Select(x => x.Month).ToArray());
            Assert.Equal(1, months.Items.Single(x => x.Month == 7).Count);
            Assert.Equal(1, months.Items.Single(x => x.Month == 8).Count);
            Assert.All(
                months.Items.Where(x => x.Month is not 7 and not 8),
                x => Assert.Equal(0, x.Count));

            var distribution = await client.GetFromJsonAsync<MembershipPlanDistribution>(
                "/api/tenant/statistics/membership-plan-distribution");
            Assert.NotNull(distribution);
            Assert.Equal(2, distribution.Total);
            Assert.Equal(100m, distribution.Items.Sum(x => x.Percentage));

            await AssertPdfAsync(
                client,
                "/api/tenant/reports/memberships.pdf",
                "gymlink-clanstva-2026-08-31.pdf",
                2);
            await AssertPdfAsync(
                client,
                "/api/tenant/reports/reservations.pdf",
                "gymlink-rezervacije-2026-08-31.pdf",
                8);

            await TransitionFirstMembershipAsync(
                connectionString,
                arenaAdmin.User.Tenant.Id,
                membership => membership.Suspend(
                    arenaAdmin.User.Id,
                    ReportingNow.UtcDateTime.AddHours(-2),
                    "Reporting status fixture"));
            await AssertMembershipSummaryAsync(client, 1, 2, 1, 100m);

            await TransitionFirstMembershipAsync(
                connectionString,
                arenaAdmin.User.Tenant.Id,
                membership => membership.Reactivate(
                    arenaAdmin.User.Id,
                    ReportingNow.UtcDateTime.AddHours(-1),
                    "Reporting status fixture"));
            await AssertMembershipSummaryAsync(client, 2, 2, 1, 100m);

            await TransitionFirstMembershipAsync(
                connectionString,
                arenaAdmin.User.Tenant.Id,
                membership => membership.CancelByStaff(
                    arenaAdmin.User.Id,
                    ReportingNow.UtcDateTime.AddMinutes(-30),
                    "Reporting status fixture"));
            await AssertMembershipSummaryAsync(client, 1, 2, 1, 100m);

            await SetFirstMembershipStatusAsync(
                connectionString,
                arenaAdmin.User.Tenant.Id,
                MembershipStatus.Expired);
            await AssertMembershipSummaryAsync(client, 1, 2, 1, 100m);

            await AddOverlappingReplacementMembershipAsync(
                connectionString,
                arenaAdmin.User.Tenant.Id,
                arenaAdmin.User.Id);
            await AssertMembershipSummaryAsync(client, 2, 2, 1, 100m);

            await ApplyPeriodBoundaryFixtureAsync(
                connectionString,
                arenaAdmin.User.Tenant.Id);
            await AssertMembershipSummaryAsync(client, 1, 1, 1, 0m);

            var central = await LoginAsync(client, "centraladmin");
            Authorize(client, central);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await client.GetAsync("/api/tenant/statistics/summary")).StatusCode);
            var system = await client.GetFromJsonAsync<SystemStatisticsSummary>(
                "/api/admin/statistics/summary");
            Assert.NotNull(system);
            Assert.Equal(6, system.TotalGyms);
            Assert.Equal(23, system.ActiveUsers);
            Assert.Equal(30, system.ReservationCount);
            Assert.Equal(system.TotalGyms, system.GymStatusDistribution.Sum(x => x.Count));
            var trendsJson = await client.GetStringAsync("/api/admin/statistics/trends");
            Assert.DoesNotContain("member", trendsJson, StringComparison.OrdinalIgnoreCase);
            var trends = System.Text.Json.JsonSerializer.Deserialize<SystemStatisticsTrends>(
                trendsJson,
                WebJsonOptions);
            Assert.NotNull(trends);
            Assert.Equal(6, trends.ReservationsByMonth.Count);
            Assert.Equal(30, trends.ReservationsByMonth.Sum(x => x.Count));
            Assert.Equal(
                1,
                trends.ReservationsByMonth.Single(x => x.Month == 7).Count);
            Assert.Equal(
                29,
                trends.ReservationsByMonth.Single(x => x.Month == 8).Count);

            await using var verification = CreateContext(connectionString);
            Assert.True(await verification.SecurityAuditRecords.AnyAsync(
                x => x.Action == "report.memberships_generated" &&
                     x.TargetTenantId == arenaAdmin.User.Tenant!.Id));
            Assert.True(await verification.SecurityAuditRecords.AnyAsync(
                x => x.Action == "report.reservations_generated" &&
                     x.TargetTenantId == arenaAdmin.User.Tenant!.Id));
            Assert.True(await verification.SecurityAuditRecords.AnyAsync(
                x => x.Action == "statistics.system_viewed" &&
                     x.TargetTenantId == null));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task AssertPdfAsync(
        HttpClient client,
        string path,
        string fileName,
        int expectedRows)
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(fileName, response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal(expectedRows.ToString(System.Globalization.CultureInfo.InvariantCulture), response.Headers.GetValues(
            "X-Report-Record-Count").Single());
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
    }

    private static async Task AssertMembershipSummaryAsync(
        HttpClient client,
        int activeMemberCount,
        int membershipPeriodCount,
        int previousMonthEndMembershipPeriodCount,
        decimal membershipPeriodChangePercentage)
    {
        var summary = await client.GetFromJsonAsync<TenantStatisticsSummary>(
            "/api/tenant/statistics/summary");
        Assert.NotNull(summary);
        Assert.Equal(activeMemberCount, summary.ActiveMemberCount);
        Assert.Equal(membershipPeriodCount, summary.MembershipPeriodCount);
        Assert.Equal(
            previousMonthEndMembershipPeriodCount,
            summary.PreviousMonthEndMembershipPeriodCount);
        Assert.Equal(
            membershipPeriodChangePercentage,
            summary.MembershipPeriodChangePercentage);
    }

    private static async Task TransitionFirstMembershipAsync(
        string connectionString,
        Guid tenantId,
        Action<Membership> transition)
    {
        await using var context = CreateContext(connectionString);
        var membership = await context.Memberships.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Id)
            .FirstAsync();
        transition(membership);
        await context.SaveChangesAsync();
    }

    private static async Task SetFirstMembershipStatusAsync(
        string connectionString,
        Guid tenantId,
        MembershipStatus status)
    {
        await using var context = CreateContext(connectionString);
        var membership = await context.Memberships.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Id)
            .FirstAsync();
        context.Entry(membership)
            .Property(nameof(Membership.Status))
            .CurrentValue = status;
        await context.SaveChangesAsync();
    }

    private static async Task AddOverlappingReplacementMembershipAsync(
        string connectionString,
        Guid tenantId,
        Guid actorUserId)
    {
        await using var context = CreateContext(connectionString);
        var previous = await context.Memberships.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.Status == MembershipStatus.Expired)
            .OrderBy(x => x.Id)
            .FirstAsync();
        var activatedAtUtc = ReportingNow.UtcDateTime.AddDays(-1);
        var request = new MembershipRequest
        {
            TenantId = previous.TenantId,
            MemberUserId = previous.MemberUserId,
            GymId = previous.GymId,
            MembershipPlanId = previous.MembershipPlanId,
            PaymentMethod = MembershipPaymentMethod.PayInPerson,
            RequestedAtUtc = activatedAtUtc.AddMinutes(-1),
        };
        request.Approve(actorUserId, activatedAtUtc);
        var replacement = new Membership(
            previous.TenantId,
            previous.MemberUserId,
            previous.GymId,
            previous.MembershipPlanId,
            request.Id,
            previous.PlanName,
            previous.DurationDays,
            previous.Price,
            previous.Currency,
            actorUserId,
            activatedAtUtc);
        context.MembershipRequests.Add(request);
        context.Memberships.Add(replacement);
        await context.SaveChangesAsync();
    }

    private static async Task ApplyPeriodBoundaryFixtureAsync(
        string connectionString,
        Guid tenantId)
    {
        await using var context = CreateContext(connectionString);
        var expiredMemberUserId = await context.Memberships.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.Status == MembershipStatus.Expired)
            .Select(x => x.MemberUserId)
            .SingleAsync();
        var activeMemberships = await context.Memberships.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.Status == MembershipStatus.Active)
            .ToListAsync();
        Assert.Equal(2, activeMemberships.Count);
        var startsAtBoundary = activeMemberships.Single(
            x => x.MemberUserId == expiredMemberUserId);
        var endsAtBoundary = activeMemberships.Single(
            x => x.MemberUserId != expiredMemberUserId);
        context.Entry(startsAtBoundary)
            .Property(nameof(Membership.StartsAtUtc))
            .CurrentValue = ReportingNow.UtcDateTime;
        context.Entry(startsAtBoundary)
            .Property(nameof(Membership.EndsAtUtc))
            .CurrentValue = ReportingNow.UtcDateTime.AddDays(startsAtBoundary.DurationDays);
        context.Entry(endsAtBoundary)
            .Property(nameof(Membership.EndsAtUtc))
            .CurrentValue = ReportingNow.UtcDateTime;
        await context.SaveChangesAsync();
    }

    private static async Task ApplyMonthBoundaryFixtureAsync(
        string connectionString,
        Guid arenaTenantId)
    {
        await using var context = CreateContext(connectionString);
        var memberships = await context.Memberships.IgnoreQueryFilters()
            .Where(x => x.TenantId == arenaTenantId)
            .OrderBy(x => x.Id)
            .Take(2)
            .ToListAsync();
        Assert.Equal(2, memberships.Count);
        SetMembershipPeriod(
            context,
            memberships[0],
            new DateTime(2026, 7, 31, 21, 59, 59, DateTimeKind.Utc));
        SetMembershipPeriod(
            context,
            memberships[1],
            new DateTime(2026, 7, 31, 22, 30, 0, DateTimeKind.Utc));

        var reservations = await context.AppointmentReservations.IgnoreQueryFilters()
            .OrderBy(x => x.StartsAtUtc)
            .Take(4)
            .ToListAsync();
        Assert.Equal(4, reservations.Count);
        var boundaryStarts = new[]
        {
            new DateTime(2026, 7, 31, 21, 59, 59, DateTimeKind.Utc),
            new DateTime(2026, 7, 31, 22, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 31, 22, 0, 1, DateTimeKind.Utc),
            new DateTime(2026, 7, 31, 22, 30, 0, DateTimeKind.Utc),
        };
        for (var index = 0; index < reservations.Count; index++)
        {
            var reservation = reservations[index];
            context.Entry(reservation)
                .Property(nameof(AppointmentReservation.StartsAtUtc))
                .CurrentValue = boundaryStarts[index];
            context.Entry(reservation)
                .Property(nameof(AppointmentReservation.EndsAtUtc))
                .CurrentValue = boundaryStarts[index].AddMinutes(reservation.DurationMinutes);
        }

        await context.SaveChangesAsync();
    }

    private static void SetMembershipPeriod(
        GymLinkDbContext context,
        Membership membership,
        DateTime startsAtUtc)
    {
        context.Entry(membership)
            .Property(nameof(Membership.StartsAtUtc))
            .CurrentValue = startsAtUtc;
        context.Entry(membership)
            .Property(nameof(Membership.EndsAtUtc))
            .CurrentValue = startsAtUtc.AddDays(membership.DurationDays);
    }

    private static async Task<AuthSessionDto> LoginAsync(HttpClient client, string identifier)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { identifier, password = Password });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthSessionDto>()
            ?? throw new InvalidOperationException("Login returned no session.");
    }

    private static void Authorize(HttpClient client, AuthSessionDto session) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:GymLink", connectionString);
            builder.UseSetting("Jwt:Issuer", "GymLink.Tests");
            builder.UseSetting("Jwt:Audience", "GymLink.Tests.Client");
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("Jwt:AccessTokenMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenDays", "30");
            builder.UseSetting(
                "PasswordReset:CodePepper",
                "integration-test-reset-pepper-at-least-32-bytes");
            builder.UseSetting("Seed:Enabled", "true");
            builder.UseSetting("Seed:DefaultPassword", Password);
            builder.UseSetting("RabbitMq:Enabled", "false");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:GymLink"] = connectionString,
                    ["Jwt:Issuer"] = "GymLink.Tests",
                    ["Jwt:Audience"] = "GymLink.Tests.Client",
                    ["Jwt:SigningKey"] = SigningKey,
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Jwt:RefreshTokenDays"] = "30",
                    ["PasswordReset:CodePepper"] =
                        "integration-test-reset-pepper-at-least-32-bytes",
                    ["Seed:Enabled"] = "true",
                    ["Seed:DefaultPassword"] = Password,
                    ["RabbitMq:Enabled"] = "false",
                }));
            builder.ConfigureServices(services =>
            {
                var timeProvider = new FixedTimeProvider(ReportingNow);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(timeProvider);
                services.PostConfigure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options => options.TokenValidationParameters.LifetimeValidator =
                        (notBefore, expires, _, _) =>
                            (!notBefore.HasValue || notBefore <= ReportingNow.UtcDateTime) &&
                            expires.HasValue && expires >= ReportingNow.UtcDateTime);
            });
        });

    private static GymLinkDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GymLinkDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new GymLinkDbContext(options, new TestTenantContext(null));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
