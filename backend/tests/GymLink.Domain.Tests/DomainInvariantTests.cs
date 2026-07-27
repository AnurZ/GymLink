using GymLink.Domain.Common;
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
}
