using GymLink.Domain.Enums;
using GymLink.Infrastructure.Memberships;
using GymLink.Infrastructure.Reservations;

namespace GymLink.IntegrationTests;

public sealed class NotificationTextTests
{
    [Theory]
    [InlineData("membership.requested", "Oxide Gym", "Mjesečna")]
    [InlineData("membership.request.cancelled", "Oxide Gym", "Mjesečna")]
    [InlineData("membership.approved", "Oxide Gym", "Mjesečna")]
    [InlineData("membership.rejected", "Nedostaje dokumentacija", "Mjesečna")]
    [InlineData("membership.cancelled", "Anur Zjakić", "Oxide Gym")]
    [InlineData("membership.suspended", "Nedostaje dokumentacija", "Oxide Gym")]
    [InlineData("membership.reactivated", "Anur Zjakić", "Oxide Gym")]
    [InlineData("membership.expired", "Anur Zjakić", "Oxide Gym")]
    public void Membership_notifications_are_contextual(
        string eventName,
        string expectedOne,
        string expectedTwo)
    {
        var details = new LoggingMembershipWorkflowEventRecorder.MembershipNotificationDetails(
            "Anur Zjakić",
            "Oxide Gym",
            "Mjesečna",
            "Nedostaje dokumentacija",
            MembershipPaymentMethod.PayInPerson,
            true);

        var memberText = LoggingMembershipWorkflowEventRecorder.Format(
            eventName,
            details,
            true);
        var adminText = LoggingMembershipWorkflowEventRecorder.Format(
            eventName,
            details,
            false);

        Assert.Contains(expectedOne, memberText + adminText, StringComparison.Ordinal);
        Assert.Contains(expectedTwo, memberText + adminText, StringComparison.Ordinal);
        Assert.DoesNotContain("Status članstva je ažuriran", memberText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("reservation.created", ReservationStatus.Pending, "zakazan")]
    [InlineData("reservation.confirmed_pay_in_person", ReservationStatus.Confirmed, "potvrđen")]
    [InlineData("reservation.status_changed", ReservationStatus.Completed, "završen")]
    [InlineData("reservation.status_changed", ReservationStatus.Cancelled, "otkazan")]
    public void Reservation_notifications_include_people_service_and_sarajevo_time(
        string eventName,
        ReservationStatus status,
        string expectedStatus)
    {
        var details = new LoggingReservationWorkflowEventRecorder.ReservationNotificationDetails(
            "Anur Zjakić",
            "Trener Test",
            Guid.NewGuid(),
            "Oxide Gym",
            "Individualni trening",
            new DateTime(2026, 8, 14, 16, 30, 0, DateTimeKind.Utc),
            status,
            status == ReservationStatus.Cancelled ? "Bolest trenera" : null);

        foreach (var role in Enum.GetValues<LoggingReservationWorkflowEventRecorder.NotificationRole>())
        {
            var text = LoggingReservationWorkflowEventRecorder.FormatReservation(
                eventName,
                details,
                role);
            Assert.Contains("Individualni trening", text, StringComparison.Ordinal);
            Assert.Contains("14.08.2026.", text, StringComparison.Ordinal);
            Assert.Contains("18:30", text, StringComparison.Ordinal);
            Assert.Contains(expectedStatus, text, StringComparison.Ordinal);
        }
    }
}
