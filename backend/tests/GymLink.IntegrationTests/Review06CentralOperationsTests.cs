using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Application.Memberships;
using GymLink.Application.Reservations;
using GymLink.Contracts.Messaging.V1;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Memberships;
using GymLink.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GymLink.IntegrationTests;

public sealed class Review06CentralOperationsTests
{
    private const string Password = "Test123!";
    private const string SigningKey = "integration-test-signing-key-at-least-32-bytes";

    [Fact]
    public async Task CentralAdmin_operations_are_gym_scoped_audited_and_preserve_GymAdmin_actions()
    {
        var databaseName = $"GymLink_Review06CentralOperations_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        try
        {
            await using (var migration = CreateContext(connectionString))
            {
                await migration.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

            var centralAdmin = await LoginAsync(client, "centraladmin");
            var gymAdmin = await LoginAsync(client, "admin.respect");
            var centralMember = await RegisterAsync(client, "Central Cash Member");
            var gymAdminMember = await RegisterAsync(client, "GymAdmin Cash Member");
            var stripeMember = await RegisterAsync(client, "Stripe Pending Member");

            Guid gymId;
            Guid otherGymId;
            Guid tenantId;
            Guid planId;
            Guid confirmedReservationId;
            Guid otherGymReservationId;
            await using (var setup = CreateContext(connectionString))
            {
                var gym = await setup.Gyms.IgnoreQueryFilters()
                    .SingleAsync(x => x.Name == "Sportska Akademija Respect");
                gymId = gym.Id;
                tenantId = gym.TenantId;
                planId = await setup.MembershipPlans.IgnoreQueryFilters()
                    .Where(x => x.GymId == gymId && x.IsActive)
                    .Select(x => x.Id)
                    .FirstAsync();
                otherGymId = await setup.Gyms.IgnoreQueryFilters()
                    .Where(x => x.Id != gymId)
                    .Select(x => x.Id)
                    .FirstAsync();
                confirmedReservationId = await (
                        from seededReservation in setup.AppointmentReservations.IgnoreQueryFilters()
                        join seededMembership in setup.Memberships.IgnoreQueryFilters()
                            on seededReservation.MembershipId equals seededMembership.Id
                        where seededMembership.GymId == gymId &&
                              seededReservation.Status == ReservationStatus.Confirmed
                        select seededReservation.Id)
                    .FirstAsync();
                otherGymReservationId = await (
                        from seededReservation in setup.AppointmentReservations.IgnoreQueryFilters()
                        join seededMembership in setup.Memberships.IgnoreQueryFilters()
                            on seededReservation.MembershipId equals seededMembership.Id
                        where seededMembership.GymId == otherGymId &&
                              seededReservation.Status == ReservationStatus.Confirmed
                        select seededReservation.Id)
                    .FirstAsync();
            }

            var centralCash = await CreateCashRequestAsync(client, centralMember, planId);
            var tenantCash = await CreateCashRequestAsync(client, gymAdminMember, planId);

            client.DefaultRequestHeaders.Authorization = null;
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await client.GetAsync(
                    $"/api/admin/gyms/{gymId}/membership-requests?page=1&pageSize=10"))
                .StatusCode);

            Authorize(client, gymAdmin);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await client.GetAsync(
                    $"/api/admin/gyms/{gymId}/membership-requests?page=1&pageSize=10"))
                .StatusCode);

            var tenantPage = await client.GetFromJsonAsync<PagedResult<MembershipRequestDto>>(
                "/api/tenant/membership-requests?paymentCategory=PayInPerson&page=1&pageSize=100");
            var tenantRequest = tenantPage!.Items.Single(x => x.Id == tenantCash.Id);
            Assert.Equal(["approve", "reject", "view"], tenantRequest.AllowedActions);
            var tenantApproval = await client.PostAsJsonAsync(
                $"/api/tenant/membership-requests/{tenantRequest.Id}/approve",
                new { concurrencyToken = tenantRequest.ConcurrencyToken });
            tenantApproval.EnsureSuccessStatusCode();

            await using (var setup = CreateContext(connectionString))
            {
                setup.MembershipRequests.Add(new MembershipRequest
                {
                    TenantId = tenantId,
                    MemberUserId = stripeMember.User.Id,
                    GymId = gymId,
                    MembershipPlanId = planId,
                    PaymentMethod = MembershipPaymentMethod.Stripe,
                    RequestedAtUtc = DateTime.UtcNow,
                });
                await setup.SaveChangesAsync();
            }

            Authorize(client, centralAdmin);
            var membershipPage = await client.GetFromJsonAsync<PagedResult<MembershipRequestDto>>(
                $"/api/admin/gyms/{gymId}/membership-requests" +
                "?paymentCategory=PayInPerson&member=Central%20Cash%20Member&page=1&pageSize=10");
            var centralRow = Assert.Single(membershipPage!.Items);
            Assert.Equal(centralCash.Id, centralRow.Id);
            Assert.Equal(["confirmCashPayment"], centralRow.AllowedActions);

            var crossGymCash = await client.PostAsJsonAsync(
                $"/api/admin/gyms/{otherGymId}/membership-requests/{centralCash.Id}/confirm-cash",
                new { concurrencyToken = centralRow.ConcurrencyToken });
            Assert.Equal(HttpStatusCode.NotFound, crossGymCash.StatusCode);

            var stripePage = await client.GetFromJsonAsync<PagedResult<MembershipRequestDto>>(
                $"/api/admin/gyms/{gymId}/membership-requests" +
                "?paymentCategory=Stripe&member=Stripe%20Pending%20Member&page=1&pageSize=10");
            var stripeRow = Assert.Single(stripePage!.Items);
            Assert.Empty(stripeRow.AllowedActions);
            var stripeConfirmation = await client.PostAsJsonAsync(
                $"/api/admin/gyms/{gymId}/membership-requests/{stripeRow.Id}/confirm-cash",
                new { concurrencyToken = stripeRow.ConcurrencyToken });
            Assert.Equal(HttpStatusCode.Conflict, stripeConfirmation.StatusCode);
            Assert.Equal(
                "membership_request_not_pay_in_person",
                await ProblemCodeAsync(stripeConfirmation));

            var cashConfirmation = await client.PostAsJsonAsync(
                $"/api/admin/gyms/{gymId}/membership-requests/{centralRow.Id}/confirm-cash",
                new { concurrencyToken = centralRow.ConcurrencyToken });
            cashConfirmation.EnsureSuccessStatusCode();
            var confirmedCash = await cashConfirmation.Content
                .ReadFromJsonAsync<MembershipRequestDto>();
            Assert.NotNull(confirmedCash?.Membership);
            Assert.Equal(MembershipStatus.Active, confirmedCash.Membership.Status);
            Assert.False(confirmedCash.Membership.IsPaid);
            Assert.Empty(confirmedCash.AllowedActions);
            Assert.Empty(confirmedCash.Membership.AllowedActions);

            var reservations = await client.GetFromJsonAsync<PagedResult<ReservationDto>>(
                $"/api/admin/gyms/{gymId}/reservations" +
                $"?status={(int)ReservationStatus.Confirmed}&page=1&pageSize=100");
            var reservation = reservations!.Items.Single(x => x.Id == confirmedReservationId);
            Assert.Equal(["complete"], reservation.AllowedActions);
            Assert.DoesNotContain(reservations.Items, x => x.Id == otherGymReservationId);

            var crossGymCompletion = await client.PostAsJsonAsync(
                $"/api/admin/gyms/{otherGymId}/reservations/{confirmedReservationId}/complete",
                new { concurrencyToken = reservation.ConcurrencyToken });
            Assert.Equal(HttpStatusCode.NotFound, crossGymCompletion.StatusCode);

            var completion = await client.PostAsJsonAsync(
                $"/api/admin/gyms/{gymId}/reservations/{confirmedReservationId}/complete",
                new { concurrencyToken = reservation.ConcurrencyToken });
            completion.EnsureSuccessStatusCode();
            var completed = await completion.Content.ReadFromJsonAsync<ReservationDto>();
            Assert.Equal(ReservationStatus.Completed, completed?.Status);
            Assert.Empty(completed!.AllowedActions);

            var repeatedCompletion = await client.PostAsJsonAsync(
                $"/api/admin/gyms/{gymId}/reservations/{confirmedReservationId}/complete",
                new { concurrencyToken = completed.ConcurrencyToken });
            Assert.Equal(HttpStatusCode.Conflict, repeatedCompletion.StatusCode);

            await using var verification = CreateContext(connectionString);
            var membership = await verification.Memberships.IgnoreQueryFilters()
                .SingleAsync(x => x.MembershipRequestId == centralCash.Id);
            Assert.Equal(MembershipStatus.Active, membership.Status);
            Assert.Null(membership.PaymentId);
            Assert.False(await verification.Payments.IgnoreQueryFilters()
                .AnyAsync(x => x.TargetId == membership.Id));
            Assert.True(await verification.UserGymAssignments.IgnoreQueryFilters().AnyAsync(
                x => x.UserId == centralMember.User.Id &&
                     x.TenantId == tenantId &&
                     x.Role == RoleNames.Member &&
                     x.Status == AssignmentStatus.Active));
            Assert.Equal(
                ReservationStatus.Completed,
                (await verification.AppointmentReservations.IgnoreQueryFilters()
                    .SingleAsync(x => x.Id == confirmedReservationId)).Status);
            Assert.True(await verification.ActivityHistory.AnyAsync(
                x => x.SourceId == confirmedReservationId &&
                     x.EventType == ActivityEventType.ReservationCompletion));
            Assert.True(await verification.OutboxMessages.AnyAsync(
                x => x.MessageType == MessageContractNames.NotificationRequestedV1 &&
                     x.Payload.Contains(centralCash.Id.ToString())));
            Assert.True(await verification.OutboxMessages.AnyAsync(
                x => x.MessageType == MessageContractNames.NotificationRequestedV1 &&
                     x.Payload.Contains(confirmedReservationId.ToString())));

            var auditActions = await verification.SecurityAuditRecords
                .Where(x => x.TargetTenantId == tenantId &&
                            x.ActorUserId == centralAdmin.User.Id)
                .Select(x => x.Action)
                .ToListAsync();
            Assert.Contains("central.membership_operations_viewed", auditActions);
            Assert.Contains("central.membership_cash_confirmed", auditActions);
            Assert.Contains("central.reservation_operations_viewed", auditActions);
            Assert.Contains("central.reservation_completed", auditActions);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<MembershipRequestDto> CreateCashRequestAsync(
        HttpClient client,
        AuthSessionDto member,
        Guid planId)
    {
        Authorize(client, member);
        var response = await client.PostAsJsonAsync(
            "/api/membership-requests",
            new
            {
                membershipPlanId = planId,
                paymentMethod = MembershipPaymentMethod.PayInPerson,
            });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MembershipRequestDto>()
            ?? throw new InvalidOperationException("Membership request returned no body.");
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

    private static async Task<AuthSessionDto> RegisterAsync(HttpClient client, string displayName)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var suffix = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                username = $"review06-{suffix}",
                email = $"review06-{suffix}@gymlink.local",
                displayName,
                password = Password,
            });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthSessionDto>()
            ?? throw new InvalidOperationException("Registration returned no session.");
    }

    private static void Authorize(HttpClient client, AuthSessionDto session) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("title").GetString()
            ?? throw new InvalidOperationException("Problem response had no title.");
    }

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
        });

    private static GymLinkDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GymLinkDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new GymLinkDbContext(options, new TestTenantContext(null));
    }
}
