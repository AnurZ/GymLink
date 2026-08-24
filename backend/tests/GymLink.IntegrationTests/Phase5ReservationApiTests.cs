using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymLink.Application.Catalog;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Application.Memberships;
using GymLink.Application.Messaging;
using GymLink.Application.Payments;
using GymLink.Application.Reservations;
using GymLink.Domain.Enums;
using GymLink.Domain.Trainers;
using GymLink.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
            var member = await RegisterAsync(setupClient, "Reservation Test Member");
            var secondMember = await RegisterAsync(setupClient, "Second Reservation Member");
            var admin = await LoginAsync(setupClient, "admin.respect");
            var otherAdmin = await LoginAsync(setupClient, "admin.arena");
            var trainerSession = await LoginAsync(setupClient, "respecttrainer1");

            var gymId = await FindGymAsync(setupClient, "Sportska Akademija Respect");
            var plans = await setupClient.GetFromJsonAsync<PagedResult<MembershipPlanDto>>(
                $"/api/gyms/{gymId}/membership-plans");
            var trainers = await setupClient.GetFromJsonAsync<PagedResult<TrainerDto>>(
                $"/api/gyms/{gymId}/trainers");
            Assert.NotNull(plans);
            Assert.NotNull(trainers);
            var trainer = Assert.Single(trainers.Items, x => x.DisplayName == "Emir Hadžić");
            var offerings = await setupClient.GetFromJsonAsync<PagedResult<TrainerOfferingDto>>(
                $"/api/trainers/{trainer.Id}/offerings");
            var offering = Assert.Single(
                offerings!.Items,
                x => x.Name == "Personalni trening 60 min");
            var plan = Assert.Single(plans.Items, x => x.Name == "Mjesečna članarina");

            await ActivateMembershipAsync(setupClient, member, admin, plan.Id);
            await ActivateMembershipAsync(setupClient, secondMember, admin, plan.Id);

            decimal initialGymRating;
            int initialGymReviewCount;
            decimal initialTrainerRating;
            int initialTrainerReviewCount;
            await using (var baseline = CreateContext(connectionString))
            {
                var gymBaseline = await baseline.Gyms.IgnoreQueryFilters()
                    .Where(x => x.Id == gymId)
                    .Select(x => new { x.AverageRating, x.ReviewCount })
                    .SingleAsync();
                initialGymRating = gymBaseline.AverageRating;
                initialGymReviewCount = gymBaseline.ReviewCount;
                var trainerBaseline = await baseline.TrainerProfiles.IgnoreQueryFilters()
                    .Where(x => x.Id == trainer.Id)
                    .Select(x => new { x.AverageRating, x.ReviewCount })
                    .SingleAsync();
                initialTrainerRating = trainerBaseline.AverageRating;
                initialTrainerReviewCount = trainerBaseline.ReviewCount;
            }

            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(
                TrainerAvailabilitySchedule.SarajevoTimeZoneId);
            var localDay = DateTime.SpecifyKind(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow.AddDays(2), timeZone).Date
                    .AddHours(10),
                DateTimeKind.Unspecified);
            var start = TimeZoneInfo.ConvertTimeToUtc(localDay, timeZone);
            Authorize(setupClient, admin);
            var existingSchedule = await setupClient.GetFromJsonAsync<TrainerScheduleDto>(
                $"/api/tenant/trainer-availability/schedule?trainerProfileId={trainer.Id}");
            Assert.NotNull(existingSchedule);
            var scheduleResponse = await setupClient.PutAsJsonAsync(
                "/api/tenant/trainer-availability/schedule",
                new
                {
                    trainerProfileId = trainer.Id,
                    shifts = new[]
                    {
                        new
                        {
                            dayOfWeek = (int)localDay.DayOfWeek,
                            period = (int)TrainerShiftPeriod.Morning,
                        },
                    },
                    concurrencyToken = existingSchedule.ConcurrencyToken,
                });
            scheduleResponse.EnsureSuccessStatusCode();
            var schedule = await scheduleResponse.Content.ReadFromJsonAsync<TrainerScheduleDto>();
            Assert.NotNull(schedule);
            Assert.Single(schedule.Shifts);
            var publicAvailability = await setupClient
                .GetFromJsonAsync<PagedResult<AvailabilityDto>>(
                    $"/api/trainers/{trainer.Id}/availability" +
                    $"?trainerServiceOfferingId={offering.Id}" +
                    $"&fromUtc={Uri.EscapeDataString(start.ToString("O"))}" +
                    $"&toUtc={Uri.EscapeDataString(start.AddDays(1).ToString("O"))}");
            Assert.NotNull(publicAvailability);
            Assert.Contains(publicAvailability.Items, x => x.StartsAtUtc == start);
            var localDate = DateOnly.FromDateTime(localDay);
            var calendarPath =
                $"/api/trainers/{trainer.Id}/availability-calendar" +
                $"?trainerServiceOfferingId={offering.Id}" +
                $"&fromLocalDate={localDate:yyyy-MM-dd}" +
                $"&toLocalDate={localDate:yyyy-MM-dd}";
            var openCalendar = await setupClient
                .GetFromJsonAsync<PublicAvailabilityCalendarDto>(calendarPath);
            Assert.NotNull(openCalendar);
            Assert.Equal(TrainerAvailabilitySchedule.SarajevoTimeZoneId, openCalendar.TimeZoneId);
            var openDay = Assert.Single(openCalendar.Days);
            Assert.Equal(localDate, openDay.Date);
            Assert.True(openDay.TotalSlots > 1);
            Assert.Equal(openDay.TotalSlots, openDay.AvailableSlots);
            Assert.All(openDay.Slots, slot => Assert.True(slot.IsAvailable));

            var oversizedCalendar = await setupClient.GetAsync(
                $"/api/trainers/{trainer.Id}/availability-calendar" +
                $"?trainerServiceOfferingId={offering.Id}" +
                $"&fromLocalDate={localDate:yyyy-MM-dd}" +
                $"&toLocalDate={localDate.AddDays(42):yyyy-MM-dd}");
            Assert.Equal(HttpStatusCode.BadRequest, oversizedCalendar.StatusCode);
            Assert.Equal(
                "availability_calendar_range_too_large",
                await ProblemCodeAsync(oversizedCalendar));

            var unique = Guid.NewGuid().ToString("N");
            var registration = await setupClient.PostAsJsonAsync(
                "/api/auth/register",
                new
                {
                    username = $"nomembership-{unique}",
                    email = $"nomembership-{unique}@gymlink.local",
                    displayName = "No Membership Member",
                    password = Password,
                });
            registration.EnsureSuccessStatusCode();
            var noMembership = await registration.Content.ReadFromJsonAsync<AuthSessionDto>();
            Assert.NotNull(noMembership);
            using (var noMembershipClient = factory.CreateClient())
            {
                Authorize(noMembershipClient, noMembership);
                var missingCoverage = await noMembershipClient.PostAsJsonAsync(
                    "/api/reservations",
                    new
                    {
                        startsAtUtc = start,
                        trainerServiceOfferingId = offering.Id,
                    });
                Assert.Equal(HttpStatusCode.Conflict, missingCoverage.StatusCode);
                Assert.Equal(
                    "covering_membership_required",
                    await ProblemCodeAsync(missingCoverage));
            }

            using var memberClient = factory.CreateClient();
            using var secondClient = factory.CreateClient();
            Authorize(memberClient, member);
            Authorize(secondClient, secondMember);
            var unlistedStart = await memberClient.PostAsJsonAsync(
                "/api/reservations",
                new
                {
                    startsAtUtc = start.AddMinutes(15),
                    trainerServiceOfferingId = offering.Id,
                });
            Assert.Equal(HttpStatusCode.Conflict, unlistedStart.StatusCode);
            Assert.Equal("appointment_outside_shift", await ProblemCodeAsync(unlistedStart));
            var bookingRequest = new
            {
                startsAtUtc = start,
                trainerServiceOfferingId = offering.Id,
            };
            var firstBooking = memberClient.PostAsJsonAsync("/api/reservations", bookingRequest);
            var secondBooking = secondClient.PostAsJsonAsync("/api/reservations", bookingRequest);
            var results = await Task.WhenAll(firstBooking, secondBooking);
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created);
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
            var winnerIndex = Array.FindIndex(results, x => x.StatusCode == HttpStatusCode.Created);
            var winningSession = winnerIndex == 0 ? member : secondMember;
            var reservation = await results[winnerIndex].Content.ReadFromJsonAsync<ReservationDto>();
            Assert.NotNull(reservation);
            Assert.Equal(offering.Price, reservation.Price);
            Assert.Equal(ReservationPaymentMethod.Stripe, reservation.PaymentMethod);
            Assert.Equal(ReservationStatus.Pending, reservation.Status);

            var heldCalendar = await setupClient
                .GetFromJsonAsync<PublicAvailabilityCalendarDto>(calendarPath);
            var heldDay = Assert.Single(heldCalendar!.Days);
            Assert.Equal(openDay.TotalSlots, heldDay.TotalSlots);
            Assert.Equal(openDay.AvailableSlots - 1, heldDay.AvailableSlots);
            Assert.False(Assert.Single(
                heldDay.Slots,
                slot => slot.StartsAtUtc == start).IsAvailable);

            Authorize(setupClient, winningSession);
            var pendingMemberReservations =
                await setupClient.GetFromJsonAsync<PagedResult<ReservationDto>>(
                    "/api/me/reservations?page=1&pageSize=20");
            Assert.Contains(
                pendingMemberReservations!.Items,
                item => item.Id == reservation.Id &&
                        item.Status == ReservationStatus.Pending &&
                        item.AllowedActions.Contains("pay"));
            var explicitlyPendingMemberReservations =
                await setupClient.GetFromJsonAsync<PagedResult<ReservationDto>>(
                    $"/api/me/reservations?status={(int)ReservationStatus.Pending}" +
                    "&page=1&pageSize=20");
            Assert.Contains(
                explicitlyPendingMemberReservations!.Items,
                item => item.Id == reservation.Id);

            var adjacentResponse = await setupClient.PostAsJsonAsync(
                "/api/reservations",
                new
                {
                    startsAtUtc = start.AddMinutes(offering.DurationMinutes),
                    trainerServiceOfferingId = offering.Id,
                });
            adjacentResponse.EnsureSuccessStatusCode();
            var adjacent = await adjacentResponse.Content
                .ReadFromJsonAsync<ReservationDto>();
            Assert.NotNull(adjacent);
            Assert.Equal(ReservationStatus.Pending, adjacent.Status);
            Assert.Equal(reservation.EndsAtUtc, adjacent.StartsAtUtc);
            var cancelAdjacent = await setupClient.PostAsJsonAsync(
                $"/api/me/reservations/{adjacent.Id}/cancel",
                new { concurrencyToken = adjacent.ConcurrencyToken });
            cancelAdjacent.EnsureSuccessStatusCode();

            Authorize(setupClient, trainerSession);
            var pendingTrainerReservations =
                await setupClient.GetFromJsonAsync<PagedResult<ReservationDto>>(
                    "/api/me/trainer-reservations?page=1&pageSize=20");
            Assert.DoesNotContain(
                pendingTrainerReservations!.Items,
                item => item.Id == reservation.Id);

            Authorize(setupClient, admin);
            var pendingTenantReservations =
                await setupClient.GetFromJsonAsync<PagedResult<ReservationDto>>(
                    "/api/tenant/reservations?page=1&pageSize=20");
            Assert.DoesNotContain(
                pendingTenantReservations!.Items,
                item => item.Id == reservation.Id);

            await using (var beforePayment = CreateContext(connectionString))
            {
                var payloads = await beforePayment.OutboxMessages
                    .Where(x => x.MessageType == "notification.requested.v1")
                    .Select(x => x.Payload)
                    .ToListAsync();
                Assert.DoesNotContain(
                    payloads,
                    payload =>
                    {
                        using var document = JsonDocument.Parse(payload);
                        var notification = document.RootElement.GetProperty("payload");
                        return notification.GetProperty("targetId").GetGuid() == reservation.Id;
                    });
            }

            Authorize(setupClient, otherAdmin);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await setupClient.GetAsync($"/api/tenant/reservations/{reservation.Id}")).StatusCode);

            Authorize(setupClient, admin);
            var confirm = await setupClient.PostAsJsonAsync(
                $"/api/tenant/reservations/{reservation.Id}/confirm",
                new { concurrencyToken = reservation.ConcurrencyToken });
            Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
            Assert.Equal("payment_confirmation_required", await ProblemCodeAsync(confirm));

            Authorize(setupClient, winningSession);
            var checkout = await setupClient.PostAsync(
                $"/api/payments/reservations/{reservation.Id}/checkout",
                null);
            checkout.EnsureSuccessStatusCode();
            var checkoutResult = await checkout.Content.ReadFromJsonAsync<CheckoutSessionDto>();
            Assert.NotNull(checkoutResult);
            var providerSessionId = $"cs_test_{checkoutResult.PaymentId:N}";
            var webhook = await setupClient.PostAsync(
                "/api/webhooks/stripe",
                new StringContent(providerSessionId));
            webhook.EnsureSuccessStatusCode();
            var paymentReturn = await setupClient.GetAsync(
                $"/payments/stripe/success?session_id={providerSessionId}");
            paymentReturn.EnsureSuccessStatusCode();

            await using (var afterPayment = CreateContext(connectionString))
            {
                var payloads = await afterPayment.OutboxMessages
                    .Where(x => x.MessageType == "notification.requested.v1")
                    .Select(x => x.Payload)
                    .ToListAsync();
                var recipients = payloads.Select(payload =>
                    {
                        using var document = JsonDocument.Parse(payload);
                        var notification = document.RootElement.GetProperty("payload");
                        return new
                        {
                            TargetId = notification.GetProperty("targetId").GetGuid(),
                            Category = notification.GetProperty("category").GetString(),
                            Recipient = notification.GetProperty("recipientUserId").GetGuid(),
                        };
                    })
                    .Where(x =>
                        x.TargetId == reservation.Id &&
                        x.Category == "reservation.confirmed")
                    .Select(x => x.Recipient)
                    .ToHashSet();
                Assert.Equal(
                    new HashSet<Guid>
                    {
                        winningSession.User.Id,
                        trainerSession.User.Id,
                        admin.User.Id,
                    },
                    recipients);
            }

            Authorize(setupClient, admin);
            var confirmed = await setupClient.GetFromJsonAsync<ReservationDto>(
                $"/api/tenant/reservations/{reservation.Id}");
            Assert.NotNull(confirmed);
            Assert.Equal(ReservationStatus.Confirmed, confirmed.Status);
            Assert.True(confirmed.IsPaid);
            Assert.Equal(ReservationPaymentMethod.Stripe, confirmed.PaymentMethod);

            var confirmedCalendar = await setupClient
                .GetFromJsonAsync<PublicAvailabilityCalendarDto>(calendarPath);
            var confirmedDay = Assert.Single(confirmedCalendar!.Days);
            Assert.Equal(heldDay.AvailableSlots, confirmedDay.AvailableSlots);
            Assert.False(Assert.Single(
                confirmedDay.Slots,
                slot => slot.StartsAtUtc == start).IsAvailable);

            var confirmedTenantReservations =
                await setupClient.GetFromJsonAsync<PagedResult<ReservationDto>>(
                    "/api/tenant/reservations?page=1&pageSize=20");
            Assert.Contains(
                confirmedTenantReservations!.Items,
                item => item.Id == reservation.Id &&
                        item.Status == ReservationStatus.Confirmed);

            Authorize(setupClient, trainerSession);
            var confirmedTrainerReservations =
                await setupClient.GetFromJsonAsync<PagedResult<ReservationDto>>(
                    "/api/me/trainer-reservations?page=1&pageSize=20");
            Assert.Contains(
                confirmedTrainerReservations!.Items,
                item => item.Id == reservation.Id &&
                        item.Status == ReservationStatus.Confirmed);

            Authorize(setupClient, winningSession);
            var confirmedMemberReservations =
                await setupClient.GetFromJsonAsync<PagedResult<ReservationDto>>(
                    "/api/me/reservations?page=1&pageSize=20");
            Assert.Contains(
                confirmedMemberReservations!.Items,
                item => item.Id == reservation.Id &&
                        item.Status == ReservationStatus.Confirmed);
            var stripeConversations =
                await setupClient.GetFromJsonAsync<PagedResult<ConversationDto>>(
                    "/api/me/conversations?page=1&pageSize=20");
            var stripeConversation = Assert.Single(stripeConversations!.Items);
            Assert.Equal(reservation.Id, stripeConversation.OriginatingReservationId);

            var replay = await setupClient.PostAsync(
                "/api/webhooks/stripe",
                new StringContent(providerSessionId));
            replay.EnsureSuccessStatusCode();
            await using (var replayVerification = CreateContext(connectionString))
            {
                Assert.Single(
                    await replayVerification.Conversations
                        .IgnoreQueryFilters()
                        .Where(x =>
                            x.MemberUserId == winningSession.User.Id &&
                            x.TrainerUserId == trainerSession.User.Id)
                        .ToListAsync());
            }

            var inPersonSession = winnerIndex == 0 ? secondMember : member;
            Authorize(setupClient, inPersonSession);
            var inPersonResponse = await setupClient.PostAsJsonAsync(
                "/api/reservations",
                new
                {
                    startsAtUtc = start.AddMinutes(offering.DurationMinutes),
                    trainerServiceOfferingId = offering.Id,
                    paymentMethod = (int)ReservationPaymentMethod.PayInPerson,
                });
            inPersonResponse.EnsureSuccessStatusCode();
            var inPerson =
                await inPersonResponse.Content.ReadFromJsonAsync<ReservationDto>();
            Assert.NotNull(inPerson);
            Assert.Equal(ReservationStatus.Confirmed, inPerson.Status);
            Assert.Equal(ReservationPaymentMethod.PayInPerson, inPerson.PaymentMethod);
            Assert.False(inPerson.IsPaid);
            Assert.Null(inPerson.PaymentDueAtUtc);
            Assert.Contains("cancel", inPerson.AllowedActions);

            var ownReservations =
                await setupClient.GetFromJsonAsync<PagedResult<ReservationDto>>(
                    "/api/me/reservations?page=1&pageSize=20");
            Assert.Contains(
                ownReservations!.Items,
                item =>
                    item.Id == inPerson.Id &&
                    item.Status == ReservationStatus.Confirmed &&
                    item.PaymentMethod == ReservationPaymentMethod.PayInPerson);

            Authorize(setupClient, trainerSession);
            var trainerInPersonReservations =
                await setupClient.GetFromJsonAsync<PagedResult<ReservationDto>>(
                    "/api/me/trainer-reservations?page=1&pageSize=20");
            Assert.Contains(
                trainerInPersonReservations!.Items,
                item => item.Id == inPerson.Id &&
                        item.Status == ReservationStatus.Confirmed);

            Authorize(setupClient, admin);
            var tenantInPersonReservations =
                await setupClient.GetFromJsonAsync<PagedResult<ReservationDto>>(
                    "/api/tenant/reservations?page=1&pageSize=20");
            Assert.Contains(
                tenantInPersonReservations!.Items,
                item => item.Id == inPerson.Id &&
                        item.Status == ReservationStatus.Confirmed);

            Authorize(setupClient, inPersonSession);
            var inPersonConversations =
                await setupClient.GetFromJsonAsync<PagedResult<ConversationDto>>(
                    "/api/me/conversations?page=1&pageSize=20");
            Assert.Equal(
                inPerson.Id,
                Assert.Single(inPersonConversations!.Items).OriginatingReservationId);
            var rejectedCheckout = await setupClient.PostAsync(
                $"/api/payments/reservations/{inPerson.Id}/checkout",
                null);
            Assert.Equal(HttpStatusCode.Conflict, rejectedCheckout.StatusCode);
            Assert.Equal(
                "reservation_not_awaiting_payment",
                await ProblemCodeAsync(rejectedCheckout));
            await using (var notificationVerification = CreateContext(connectionString))
            {
                var payloads = await notificationVerification.OutboxMessages
                    .Where(x => x.MessageType == "notification.requested.v1")
                    .Select(x => x.Payload)
                    .ToListAsync();
                Assert.Contains(
                    payloads,
                    payload =>
                    {
                        using var document = JsonDocument.Parse(payload);
                        var notification = document.RootElement.GetProperty("payload");
                        var text = notification.GetProperty("text").GetString()!;
                        return notification.GetProperty("recipientUserId").GetGuid() ==
                                inPersonSession.User.Id &&
                            notification.GetProperty("category").GetString() ==
                                "reservation.confirmed" &&
                            notification.GetProperty("targetId").GetGuid() ==
                                inPerson.Id &&
                            text.Contains(trainer.DisplayName) &&
                            text.Contains(offering.Name) &&
                            text.Contains("je potvrđen") &&
                            !text.Contains("Plaćanje se vrši uživo na treningu.");
                    });
            }

            var cancelInPerson = await setupClient.PostAsJsonAsync(
                $"/api/me/reservations/{inPerson.Id}/cancel",
                new { concurrencyToken = inPerson.ConcurrencyToken });
            cancelInPerson.EnsureSuccessStatusCode();
            var cancelledInPerson =
                await cancelInPerson.Content.ReadFromJsonAsync<ReservationDto>();
            Assert.NotNull(cancelledInPerson);
            Assert.Equal(ReservationStatus.Cancelled, cancelledInPerson.Status);
            var visibleMemberCancellations =
                await setupClient.GetFromJsonAsync<PagedResult<ReservationDto>>(
                    $"/api/me/reservations?status={(int)ReservationStatus.Cancelled}" +
                    "&page=1&pageSize=20");
            Assert.Contains(
                visibleMemberCancellations!.Items,
                item => item.Id == inPerson.Id);

            Authorize(setupClient, trainerSession);
            var visibleTrainerCancellations =
                await setupClient.GetFromJsonAsync<PagedResult<ReservationDto>>(
                    $"/api/me/trainer-reservations?status={(int)ReservationStatus.Cancelled}" +
                    "&page=1&pageSize=20");
            Assert.Contains(
                visibleTrainerCancellations!.Items,
                item => item.Id == inPerson.Id);

            Authorize(setupClient, admin);
            var visibleTenantCancellations =
                await setupClient.GetFromJsonAsync<PagedResult<ReservationDto>>(
                    $"/api/tenant/reservations?status={(int)ReservationStatus.Cancelled}" +
                    "&page=1&pageSize=20");
            Assert.Contains(
                visibleTenantCancellations!.Items,
                item => item.Id == inPerson.Id);

            Authorize(setupClient, inPersonSession);
            var expiringStripeResponse = await setupClient.PostAsJsonAsync(
                "/api/reservations",
                new
                {
                    startsAtUtc = start.AddMinutes(offering.DurationMinutes * 2),
                    trainerServiceOfferingId = offering.Id,
                });
            expiringStripeResponse.EnsureSuccessStatusCode();
            var expiringStripe =
                await expiringStripeResponse.Content.ReadFromJsonAsync<ReservationDto>();
            Assert.NotNull(expiringStripe);
            await using (var expiration = CreateContext(connectionString))
            {
                var expiredAt = DateTime.UtcNow.AddMinutes(-1);
                await expiration.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE [AppointmentReservations]
                    SET [Status] = {ReservationStatus.Cancelled.ToString()},
                        [PaymentDueAtUtc] = {expiredAt.AddMinutes(-1)},
                        [CancelledAtUtc] = {expiredAt},
                        [CancellationReason] = {"Payment window expired."}
                    WHERE [Id] = {expiringStripe.Id}
                    """);
            }

            var cancelledMemberReservations =
                await setupClient.GetFromJsonAsync<PagedResult<ReservationDto>>(
                    $"/api/me/reservations?status={(int)ReservationStatus.Cancelled}" +
                    "&page=1&pageSize=20");
            Assert.DoesNotContain(
                cancelledMemberReservations!.Items,
                item => item.Id == expiringStripe.Id);

            Authorize(setupClient, trainerSession);
            var cancelledTrainerReservations =
                await setupClient.GetFromJsonAsync<PagedResult<ReservationDto>>(
                    $"/api/me/trainer-reservations?status={(int)ReservationStatus.Cancelled}" +
                    "&page=1&pageSize=20");
            Assert.DoesNotContain(
                cancelledTrainerReservations!.Items,
                item => item.Id == expiringStripe.Id);

            Authorize(setupClient, admin);
            var cancelledTenantReservations =
                await setupClient.GetFromJsonAsync<PagedResult<ReservationDto>>(
                    $"/api/tenant/reservations?status={(int)ReservationStatus.Cancelled}" +
                    "&page=1&pageSize=20");
            Assert.DoesNotContain(
                cancelledTenantReservations!.Items,
                item => item.Id == expiringStripe.Id);

            await using (var elapsed = CreateContext(connectionString))
            {
                var completedAt = DateTime.UtcNow.AddMinutes(-1);
                await elapsed.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE [AppointmentReservations]
                    SET [StartsAtUtc] = {completedAt.AddMinutes(-offering.DurationMinutes)},
                        [EndsAtUtc] = {completedAt},
                        [PaymentDueAtUtc] = {completedAt.AddMinutes(-offering.DurationMinutes - 1)}
                    WHERE [Id] = {reservation.Id}
                    """);
            }

            Authorize(setupClient, admin);
            var adminDetail = await setupClient.GetFromJsonAsync<ReservationDto>(
                $"/api/tenant/reservations/{reservation.Id}");
            Assert.NotNull(adminDetail);
            var complete = await setupClient.PostAsJsonAsync(
                $"/api/tenant/reservations/{reservation.Id}/complete",
                new { concurrencyToken = adminDetail.ConcurrencyToken });
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
            Assert.Equal(
                decimal.Round(
                    ((initialGymRating * initialGymReviewCount) + 5) /
                    (initialGymReviewCount + 1),
                    2,
                    MidpointRounding.AwayFromZero),
                ratedGym.AverageRating);
            Assert.Equal(initialGymReviewCount + 1, ratedGym.ReviewCount);

            await using var verification = CreateContext(connectionString);
            Assert.Single(await verification.AppointmentReservations.IgnoreQueryFilters()
                .Where(x => x.Id == reservation.Id && x.AvailabilitySlotId == null)
                .ToListAsync());
            Assert.True(await verification.SecurityAuditRecords.AnyAsync(
                x => x.Action == "availability.schedule.replaced" &&
                     x.TargetTenantId == admin.User.Tenant!.Id));
            Assert.Equal(
                initialGymReviewCount + 1,
                await verification.GymReviews.IgnoreQueryFilters()
                    .CountAsync(x => x.GymId == gymId));
            var ratedTrainer = await verification.TrainerProfiles.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == trainer.Id);
            Assert.Equal(
                decimal.Round(
                    ((initialTrainerRating * initialTrainerReviewCount) + 4) /
                    (initialTrainerReviewCount + 1),
                    2,
                    MidpointRounding.AwayFromZero),
                ratedTrainer.AverageRating);
            Assert.Equal(initialTrainerReviewCount + 1, ratedTrainer.ReviewCount);
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
        Authorize(client, member);
        var memberships = await client.GetFromJsonAsync<PagedResult<MembershipDto>>(
            "/api/me/memberships?page=1&pageSize=100");
        Assert.NotNull(memberships);
        var pending = memberships.Items.Single(
            x => x.Status == MembershipStatus.PendingPayment);
        var checkout = await client.PostAsync(
            $"/api/payments/memberships/{pending.Id}/checkout",
            null);
        checkout.EnsureSuccessStatusCode();
        var checkoutResult = await checkout.Content.ReadFromJsonAsync<CheckoutSessionDto>();
        Assert.NotNull(checkoutResult);
        var providerSessionId = $"cs_test_{checkoutResult.PaymentId:N}";
        var webhook = await client.PostAsync(
            "/api/webhooks/stripe",
            new StringContent(providerSessionId));
        webhook.EnsureSuccessStatusCode();
        var paymentReturn = await client.GetAsync(
            $"/payments/stripe/success?session_id={providerSessionId}");
        paymentReturn.EnsureSuccessStatusCode();
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

    private static async Task<AuthSessionDto> RegisterAsync(
        HttpClient client,
        string displayName)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var suffix = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                username = $"reservation-{suffix}",
                email = $"reservation-{suffix}@gymlink.local",
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
                services.Replace(ServiceDescriptor.Singleton<
                    IPaymentGateway,
                    TestPaymentGateway>()));
        });

    private static GymLinkDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GymLinkDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new GymLinkDbContext(options, new TestTenantContext(null));
    }
}
