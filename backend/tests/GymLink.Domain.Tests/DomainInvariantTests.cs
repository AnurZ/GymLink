using GymLink.Domain.Common;
using GymLink.Domain.Catalog;
using GymLink.Domain.Engagement;
using GymLink.Domain.Enums;
using GymLink.Domain.Identity;
using GymLink.Domain.Memberships;
using GymLink.Domain.Payments;
using GymLink.Domain.Reservations;
using GymLink.Domain.Trainers;

namespace GymLink.Domain.Tests;

public sealed class DomainInvariantTests
{
    [Fact]
    public void Role_names_are_the_approved_closed_set()
    {
        Assert.Equal(
            new[] { "CentralAdmin", "GymAdmin", "Member", "Trainer" },
            RoleNames.All.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Availability_slot_requires_utc_and_positive_range()
    {
        var tenantId = Guid.NewGuid();
        var trainerId = Guid.NewGuid();
        var start = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

        var slot = new TrainerAvailabilitySlot(tenantId, trainerId, start, start.AddHours(1));

        Assert.Equal(tenantId, slot.TenantId);
        Assert.Throws<DomainException>(() =>
            new TrainerAvailabilitySlot(tenantId, trainerId, start, start));
        Assert.Throws<DomainException>(() =>
            new TrainerAvailabilitySlot(
                tenantId,
                trainerId,
                DateTime.SpecifyKind(start, DateTimeKind.Local),
                start.AddHours(1)));
    }

    [Fact]
    public void Offering_rejects_invalid_duration_and_price()
    {
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();

        Assert.Throws<DomainException>(() =>
            new TrainerServiceOffering(ids[0], ids[1], ids[2], "PT", 0, 10, "BAM"));
        Assert.Throws<DomainException>(() =>
            new TrainerServiceOffering(ids[0], ids[1], ids[2], "PT", 60, -1, "BAM"));
    }

    [Fact]
    public void Reservation_copies_authoritative_duration_and_price()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var start = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

        var reservation = new AppointmentReservation(
            ids[0], ids[1], ids[2], ids[3], ids[4], ids[5],
            start, 90, 42.50m, "BAM");

        Assert.Equal(start.AddMinutes(90), reservation.EndsAtUtc);
        Assert.Equal(90, reservation.DurationMinutes);
        Assert.Equal(42.50m, reservation.Price);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Review_rating_is_bounded(int rating)
    {
        Assert.Throws<DomainException>(() =>
            new Review(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                rating,
                null));
        Assert.Throws<DomainException>(() =>
            new GymReview(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                rating,
                null));
    }

    [Fact]
    public void Availability_slot_enforces_one_to_one_state_transitions()
    {
        var start = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var slot = new TrainerAvailabilitySlot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            start,
            start.AddHours(1));

        slot.Reserve();
        Assert.Equal(AvailabilitySlotStatus.Reserved, slot.Status);
        Assert.Equal(1, slot.Capacity);
        Assert.Throws<DomainException>(() =>
            slot.Update(start.AddHours(1), start.AddHours(2), AvailabilitySlotStatus.Available));
        Assert.Throws<DomainException>(slot.Cancel);

        slot.Release();
        slot.Cancel();
        Assert.Equal(AvailabilitySlotStatus.Cancelled, slot.Status);
    }

    [Fact]
    public void Weekly_shift_uses_fixed_local_time_presets()
    {
        var morning = new TrainerWeeklyShift(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DayOfWeek.Monday,
            TrainerShiftPeriod.Morning);
        var evening = new TrainerWeeklyShift(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DayOfWeek.Sunday,
            TrainerShiftPeriod.Evening);

        Assert.Equal(new TimeOnly(8, 0), morning.StartsAtLocal);
        Assert.Equal(new TimeOnly(15, 0), morning.EndsAtLocal);
        Assert.Equal(new TimeOnly(15, 0), evening.StartsAtLocal);
        Assert.Equal(new TimeOnly(22, 0), evening.EndsAtLocal);
        Assert.Throws<DomainException>(() =>
            new TrainerWeeklyShift(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                (DayOfWeek)7,
                TrainerShiftPeriod.Morning));
    }

    [Fact]
    public void Reservation_enforces_transitions_and_staff_reason()
    {
        var ids = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        var start = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var reservation = new AppointmentReservation(
            ids[0], ids[1], ids[2], ids[3], ids[4], ids[5],
            start, 60, 25, "BAM");

        reservation.Confirm(ids[6], start.AddHours(-1));
        reservation.Complete(ids[6], start.AddHours(-2));
        Assert.Equal(ReservationStatus.Completed, reservation.Status);
        Assert.Equal(ids[6], reservation.CompletedByUserId);

        var cancellation = new AppointmentReservation(
            ids[0], ids[1], ids[2], ids[3], ids[4], ids[5],
            start, 60, 25, "BAM");
        Assert.Throws<DomainException>(() =>
            cancellation.CancelByStaff(ids[7], start, " "));
        cancellation.CancelByMember(ids[1], start.AddMinutes(-1));
        Assert.Equal(ReservationStatus.Cancelled, cancellation.Status);
    }

    [Fact]
    public void Rating_aggregates_use_all_reviews()
    {
        var gym = new Gym();
        var trainer = new TrainerProfile();

        gym.AddReview(4);
        gym.AddReview(5);
        trainer.AddReview(2);
        trainer.AddReview(5);

        Assert.Equal(4.50m, gym.AverageRating);
        Assert.Equal(2, gym.ReviewCount);
        Assert.Equal(3.50m, trainer.AverageRating);
        Assert.Equal(2, trainer.ReviewCount);
    }

    [Fact]
    public void Refund_cannot_exceed_charged_amount()
    {
        Refund.EnsureTotalDoesNotExceedChargedAmount(100, 25, 75);

        Assert.Throws<DomainException>(() =>
            Refund.EnsureTotalDoesNotExceedChargedAmount(100, 25, 76));
    }

    [Fact]
    public void Membership_request_transitions_are_terminal_and_rejection_requires_reason()
    {
        var actor = Guid.NewGuid();
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var rejected = new MembershipRequest();

        Assert.Throws<DomainException>(() => rejected.Reject(actor, now, " "));

        rejected.Reject(actor, now, "Plan unavailable");

        Assert.Equal(MembershipRequestStatus.Rejected, rejected.Status);
        Assert.Equal("Plan unavailable", rejected.DecisionReason);
        Assert.Throws<DomainException>(() => rejected.Approve(actor, now));
    }

    [Fact]
    public void Membership_request_cancellation_records_the_member_actor()
    {
        var actor = Guid.NewGuid();
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var request = new MembershipRequest();

        request.Cancel(actor, now);

        Assert.Equal(MembershipRequestStatus.Cancelled, request.Status);
        Assert.Equal(actor, request.DecidedByUserId);
        Assert.Equal(now, request.DecidedAtUtc);
    }

    [Fact]
    public void Membership_copies_plan_snapshot_and_calculates_dates()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

        var membership = new Membership(
            ids[0], ids[1], ids[2], ids[3], ids[4],
            "Monthly", 30, 55, "BAM", ids[5], now);

        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.Equal("Monthly", membership.PlanName);
        Assert.Equal(55, membership.Price);
        Assert.Equal("BAM", membership.Currency);
        Assert.Equal(now, membership.StartsAtUtc);
        Assert.Equal(now.AddDays(30), membership.EndsAtUtc);
        Assert.Equal(ids[5], membership.StatusChangedByUserId);
        Assert.Equal(now, membership.StatusChangedAtUtc);
    }

    [Fact]
    public void Membership_enforces_core_transition_matrix_and_reasons()
    {
        var ids = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToArray();
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var membership = new Membership(
            ids[0], ids[1], ids[2], ids[3], ids[4],
            "Monthly", 30, 55, "BAM", ids[5], now);

        Assert.Throws<DomainException>(() => membership.Suspend(ids[6], now.AddDays(1), " "));
        membership.Suspend(ids[6], now.AddDays(1), "Policy hold");
        Assert.Equal(MembershipStatus.Suspended, membership.Status);
        Assert.Equal("Policy hold", membership.StatusReason);

        Assert.Throws<DomainException>(() =>
            membership.Reactivate(ids[6], now.AddDays(31), "Resolved"));
        membership.Reactivate(ids[6], now.AddDays(2), "Resolved");
        Assert.Equal(MembershipStatus.Active, membership.Status);

        Assert.Throws<DomainException>(() => membership.Expire(ids[6], now.AddDays(29)));
        membership.Expire(ids[6], now.AddDays(30));
        Assert.Equal(MembershipStatus.Expired, membership.Status);
        Assert.Throws<DomainException>(() =>
            membership.CancelByMember(ids[1], now.AddDays(30)));
    }

    [Fact]
    public void Password_reset_challenge_enforces_expiry_attempts_and_single_use()
    {
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var challenge = new PasswordResetChallenge(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "hashed-code",
            "salt",
            now,
            now.AddMinutes(15),
            "hashed-ip",
            "correlation-id");

        Assert.True(challenge.CanConfirm(now.AddMinutes(14)));
        for (var attempt = 0; attempt < 5; attempt++)
        {
            challenge.RegisterFailedAttempt(now.AddMinutes(attempt + 1));
        }

        Assert.False(challenge.CanConfirm(now.AddMinutes(6)));
        var exhausted = Assert.Throws<DomainException>(() =>
            challenge.Consume(now.AddMinutes(6)));
        Assert.Equal("password_reset_invalid", exhausted.Code);

        var consumed = new PasswordResetChallenge(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "hashed-code",
            "salt",
            now,
            now.AddMinutes(15),
            null,
            "correlation-id");
        consumed.Consume(now.AddMinutes(1));

        Assert.False(consumed.CanConfirm(now.AddMinutes(2)));
        Assert.Throws<DomainException>(() => consumed.Consume(now.AddMinutes(2)));
    }

    [Fact]
    public void Password_reset_challenge_requires_utc_and_can_be_superseded()
    {
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var challenge = new PasswordResetChallenge(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "hashed-code",
            "salt",
            now,
            now.AddMinutes(15),
            null,
            "correlation-id");

        challenge.Supersede(now.AddMinutes(1));

        Assert.False(challenge.CanConfirm(now.AddMinutes(2)));
        Assert.Throws<DomainException>(() =>
            new PasswordResetChallenge(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "hashed-code",
                "salt",
                DateTime.SpecifyKind(now, DateTimeKind.Local),
                now.AddMinutes(15),
                null,
                "correlation-id"));
    }

    [Fact]
    public void Notification_mark_read_is_utc_and_idempotent()
    {
        var firstRead = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var notification = new Notification();

        notification.MarkRead(firstRead);
        notification.MarkRead(firstRead.AddMinutes(1));

        Assert.Equal(firstRead, notification.ReadAtUtc);
        Assert.Throws<DomainException>(() =>
            new Notification().MarkRead(
                DateTime.SpecifyKind(firstRead, DateTimeKind.Local)));
    }
}
