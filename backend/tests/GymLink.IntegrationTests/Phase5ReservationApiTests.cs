using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymLink.Application.Catalog;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Application.Memberships;
using GymLink.Application.Reservations;
using GymLink.Domain.Enums;
using GymLink.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GymLink.IntegrationTests;

public sealed class Phase5ReservationApiTests
{
    private const string Password = "Test123!";
    private const string SigningKey = "integration-test-signing-key-at-least-32-bytes";

    [Fact]
    public async Task Booking_is_concurrency_safe_tenant_scoped_and_updates_real_gym_rating()
    {
        var databaseName = $"GymLink_Phase5_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        try
        {
            await using (var migration = CreateContext(connectionString))
            {
                await migration.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var setupClient = factory.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await setupClient.GetAsync("/health")).StatusCode);
            var member = await LoginAsync(setupClient, "member");
            var secondMember = await LoginAsync(setupClient, "mobile");
            var admin = await LoginAsync(setupClient, "desktop");
            var otherAdmin = await LoginAsync(setupClient, "gymadmin");
            var trainerSession = await LoginAsync(setupClient, "trainer");

            var gymId = await FindGymAsync(setupClient, "GymLink Sarajevo");
            var plans = await setupClient.GetFromJsonAsync<IReadOnlyList<MembershipPlanDto>>(
                $"/api/gyms/{gymId}/membership-plans");
            var trainers = await setupClient.GetFromJsonAsync<IReadOnlyList<TrainerDto>>(
                $"/api/gyms/{gymId}/trainers");
            Assert.NotNull(plans);
            Assert.NotNull(trainers);
            var trainer = Assert.Single(trainers);
            var offerings = await setupClient.GetFromJsonAsync<IReadOnlyList<TrainerOfferingDto>>(
                $"/api/trainers/{trainer.Id}/offerings");
            var offering = Assert.Single(offerings!);

            await ActivateMembershipAsync(setupClient, member, admin, Assert.Single(plans).Id);
            await ActivateMembershipAsync(setupClient, secondMember, admin, Assert.Single(plans).Id);

            var start = DateTime.UtcNow.AddDays(2);
            start = new DateTime(
                start.Year,
                start.Month,
                start.Day,
                10,
                0,
                0,
                DateTimeKind.Utc);
            using var firstAdminClient = factory.CreateClient();
            using var secondAdminClient = factory.CreateClient();
            Authorize(firstAdminClient, admin);
            Authorize(secondAdminClient, admin);
            var slotRequest = new
            {
                trainerProfileId = trainer.Id,
                startsAtUtc = start,
                endsAtUtc = start.AddMinutes(offering.DurationMinutes),
                status = AvailabilitySlotStatus.Available,
            };
            var slotResults = await Task.WhenAll(
                firstAdminClient.PostAsJsonAsync("/api/tenant/trainer-availability", slotRequest),
                secondAdminClient.PostAsJsonAsync("/api/tenant/trainer-availability", slotRequest));
            Assert.Single(slotResults, x => x.StatusCode == HttpStatusCode.Created);
            Assert.Single(slotResults, x => x.StatusCode == HttpStatusCode.Conflict);
            var slotResponse = slotResults.Single(x => x.StatusCode == HttpStatusCode.Created);
            var slot = await slotResponse.Content.ReadFromJsonAsync<AvailabilityDto>();
            Assert.NotNull(slot);

            using var memberClient = factory.CreateClient();
            using var secondClient = factory.CreateClient();
            Authorize(memberClient, member);
            Authorize(secondClient, secondMember);
            var firstBooking = memberClient.PostAsJsonAsync(
                "/api/reservations",
                new
                {
                    trainerServiceOfferingId = offering.Id,
                    availabilitySlotId = slot.Id,
                });
            var secondBooking = secondClient.PostAsJsonAsync(
                "/api/reservations",
                new
                {
                    trainerServiceOfferingId = offering.Id,
                    availabilitySlotId = slot.Id,
                });
            var results = await Task.WhenAll(firstBooking, secondBooking);
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created);
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
            var winnerIndex = Array.FindIndex(results, x => x.StatusCode == HttpStatusCode.Created);
            var winningSession = winnerIndex == 0 ? member : secondMember;
            var reservation = await results[winnerIndex].Content.ReadFromJsonAsync<ReservationDto>();
            Assert.NotNull(reservation);
            Assert.Equal(offering.Price, reservation.Price);
            Assert.Equal(ReservationStatus.Pending, reservation.Status);

            Authorize(setupClient, otherAdmin);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await setupClient.GetAsync($"/api/tenant/reservations/{reservation.Id}")).StatusCode);

            Authorize(setupClient, admin);
            var confirm = await setupClient.PostAsJsonAsync(
                $"/api/tenant/reservations/{reservation.Id}/confirm",
                new { concurrencyToken = reservation.ConcurrencyToken });
            confirm.EnsureSuccessStatusCode();
            var confirmed = await confirm.Content.ReadFromJsonAsync<ReservationDto>();
            Assert.NotNull(confirmed);
            Assert.Equal(ReservationStatus.Confirmed, confirmed.Status);

            await using (var elapsed = CreateContext(connectionString))
            {
                var completedAt = DateTime.UtcNow.AddMinutes(-1);
                await elapsed.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE [AppointmentReservations]
                    SET [StartsAtUtc] = {completedAt.AddMinutes(-offering.DurationMinutes)},
                        [EndsAtUtc] = {completedAt}
                    WHERE [Id] = {reservation.Id}
                    """);
            }

            Authorize(setupClient, trainerSession);
            var trainerDetail = await setupClient.GetFromJsonAsync<ReservationDto>(
                $"/api/me/trainer-reservations/{reservation.Id}");
            Assert.NotNull(trainerDetail);
            var complete = await setupClient.PostAsJsonAsync(
                $"/api/tenant/reservations/{reservation.Id}/complete",
                new { concurrencyToken = trainerDetail.ConcurrencyToken });
            complete.EnsureSuccessStatusCode();
            var completed = await complete.Content.ReadFromJsonAsync<ReservationDto>();
            Assert.NotNull(completed);
            Assert.Equal(ReservationStatus.Completed, completed.Status);

            Authorize(setupClient, winningSession);
            var trainerReview = await setupClient.PostAsJsonAsync(
                $"/api/reservations/{reservation.Id}/review",
                new { rating = 4, comment = "Odličan trener." });
            trainerReview.EnsureSuccessStatusCode();

            Authorize(setupClient, winningSession);
            var review = await setupClient.PostAsJsonAsync(
                $"/api/gyms/{gymId}/reviews",
                new { rating = 5, comment = "Odlična teretana." });
            review.EnsureSuccessStatusCode();
            var duplicate = await setupClient.PostAsJsonAsync(
                $"/api/gyms/{gymId}/reviews",
                new { rating = 4, comment = "Duplicate" });
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal("gym_review_exists", await ProblemCodeAsync(duplicate));

            setupClient.DefaultRequestHeaders.Authorization = null;
            var ratedGym = await setupClient.GetFromJsonAsync<GymDetailsDto>(
                $"/api/gyms/{gymId}");
            Assert.NotNull(ratedGym);
            Assert.Equal(5, ratedGym.AverageRating);
            Assert.Equal(1, ratedGym.ReviewCount);

            await using var verification = CreateContext(connectionString);
            Assert.Equal(
                AvailabilitySlotStatus.Reserved,
                (await verification.TrainerAvailabilitySlots.IgnoreQueryFilters()
                    .SingleAsync(x => x.Id == slot.Id)).Status);
            Assert.Single(await verification.AppointmentReservations.IgnoreQueryFilters()
                .Where(x => x.AvailabilitySlotId == slot.Id)
                .ToListAsync());
            Assert.True(await verification.SecurityAuditRecords.AnyAsync(
                x => x.Action == "availability.created" &&
                     x.TargetTenantId == admin.User.Tenant!.Id));
            Assert.Single(await verification.GymReviews.IgnoreQueryFilters()
                .Where(x => x.GymId == gymId)
                .ToListAsync());
            var ratedTrainer = await verification.TrainerProfiles.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == trainer.Id);
            Assert.Equal(4, ratedTrainer.AverageRating);
            Assert.Equal(1, ratedTrainer.ReviewCount);
            Assert.Single(await verification.Reviews.IgnoreQueryFilters()
                .Where(x => x.ReservationId == reservation.Id)
                .ToListAsync());
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task ActivateMembershipAsync(
        HttpClient client,
        AuthSessionDto member,
        AuthSessionDto admin,
        Guid planId)
    {
        Authorize(client, member);
        var create = await client.PostAsJsonAsync(
            "/api/membership-requests",
            new { membershipPlanId = planId });
        create.EnsureSuccessStatusCode();
        var request = await create.Content.ReadFromJsonAsync<MembershipRequestDto>();
        Assert.NotNull(request);
        Authorize(client, admin);
        var approve = await client.PostAsJsonAsync(
            $"/api/tenant/membership-requests/{request.Id}/approve",
            new { concurrencyToken = request.ConcurrencyToken });
        approve.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> FindGymAsync(
        HttpClient client,
        string name)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var response = JsonDocument.Parse(
            await client.GetStringAsync($"/api/gyms?query={Uri.EscapeDataString(name)}"));
        var item = response.RootElement.GetProperty("items")[0];
        return item.GetProperty("id").GetGuid();
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
            builder.UseSetting("Seed:Enabled", "true");
            builder.UseSetting("Seed:DefaultPassword", Password);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:GymLink"] = connectionString,
                    ["Jwt:Issuer"] = "GymLink.Tests",
                    ["Jwt:Audience"] = "GymLink.Tests.Client",
                    ["Jwt:SigningKey"] = SigningKey,
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Jwt:RefreshTokenDays"] = "30",
                    ["Seed:Enabled"] = "true",
                    ["Seed:DefaultPassword"] = Password,
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
