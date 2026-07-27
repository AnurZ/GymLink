using GymLink.Domain.Common;
using GymLink.Domain.Enums;
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
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var start = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

        var reservation = new AppointmentReservation(
            ids[0], ids[1], ids[2], ids[3], start, 90, 42.50m, "BAM");

        Assert.Equal(start.AddMinutes(90), reservation.EndsAtUtc);
        Assert.Equal(90, reservation.DurationMinutes);
        Assert.Equal(42.50m, reservation.Price);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Review_rating_is_bounded(int rating)
    {
        var review = new Review();

        Assert.Throws<DomainException>(() => review.SetRating(rating));
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
}
