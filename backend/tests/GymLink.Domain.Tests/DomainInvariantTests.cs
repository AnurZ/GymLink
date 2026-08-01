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
    public void Trainer_image_metadata_is_atomic_bounded_and_removable()
    {
        var trainer = new TrainerProfile();

        trainer.SetImage(
            "trainers/opaque.webp",
            "/uploads/trainer-images/opaque.webp",
            "image/webp",
            TrainerProfile.MaximumImageFileSizeBytes);

        Assert.Equal("trainers/opaque.webp", trainer.ImageStorageKey);
        Assert.Equal("/uploads/trainer-images/opaque.webp", trainer.ImageUrl);
        Assert.Equal("image/webp", trainer.ImageContentType);
        Assert.Equal(TrainerProfile.MaximumImageFileSizeBytes, trainer.ImageFileSizeBytes);
        Assert.True(trainer.RemoveImage());
        Assert.False(trainer.RemoveImage());
        Assert.Null(trainer.ImageStorageKey);
        Assert.Null(trainer.ImageUrl);
        Assert.Null(trainer.ImageContentType);
        Assert.Null(trainer.ImageFileSizeBytes);
    }

    [Theory]
    [InlineData("../escape.jpg", "/uploads/trainer-images/image.jpg", "image/jpeg", 10)]
    [InlineData("image.jpg", "https://example.test/image.jpg", "image/jpeg", 10)]
    [InlineData("image.gif", "/uploads/trainer-images/image.gif", "image/gif", 10)]
    [InlineData("image.jpg", "/uploads/trainer-images/image.jpg", "image/jpeg", 0)]
    public void Trainer_image_rejects_invalid_metadata(
        string storageKey,
        string imageUrl,
        string contentType,
        long size)
    {
        var error = Assert.Throws<DomainException>(() =>
            new TrainerProfile().SetImage(storageKey, imageUrl, contentType, size));

        Assert.Equal("invalid_trainer_image", error.Code);
    }

    [Fact]
    public void Gym_image_managed_metadata_and_gallery_position_are_bounded()
    {
        var image = new GymImage();

        image.SetManagedContent(
            "opaque.webp",
            "/uploads/gym-images/opaque.webp",
            "image/webp",
            GymImage.MaximumFileSizeBytes);
        image.SetGalleryPosition(0, isPrimary: true);

        Assert.Equal(5, GymImage.MaximumGalleryImages);
        Assert.Equal("opaque.webp", image.StorageKey);
        Assert.Equal("/uploads/gym-images/opaque.webp", image.PublicUrl);
        Assert.Equal("image/webp", image.ContentType);
        Assert.Equal(GymImage.MaximumFileSizeBytes, image.FileSizeBytes);
        Assert.Equal(0, image.SortOrder);
        Assert.True(image.IsPrimary);
    }

    [Fact]
    public void Gym_image_preserves_legacy_external_metadata_shape()
    {
        var image = new GymImage
        {
            StorageKey = "seed/gym/primary",
            PublicUrl = "https://images.example.test/gym.jpg",
            AltText = "Legacy gym image",
            SortOrder = 0,
            IsPrimary = true,
        };

        Assert.Null(image.ContentType);
        Assert.Null(image.FileSizeBytes);
    }

    [Theory]
    [InlineData("../escape.jpg", "/uploads/gym-images/image.jpg", "image/jpeg", 10)]
    [InlineData("image.jpg", "https://example.test/image.jpg", "image/jpeg", 10)]
    [InlineData("image.gif", "/uploads/gym-images/image.gif", "image/gif", 10)]
    [InlineData("image.jpg", "/uploads/gym-images/image.jpg", "image/jpeg", 0)]
    public void Gym_image_rejects_invalid_managed_metadata(
        string storageKey,
        string publicUrl,
        string contentType,
        long size)
    {
        var error = Assert.Throws<DomainException>(() =>
            new GymImage().SetManagedContent(storageKey, publicUrl, contentType, size));

        Assert.Equal("invalid_gym_image", error.Code);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Gym_image_rejects_invalid_gallery_position(int sortOrder, bool isPrimary)
    {
        var error = Assert.Throws<DomainException>(() =>
            new GymImage().SetGalleryPosition(sortOrder, isPrimary));

        Assert.Equal("invalid_gym_image", error.Code);
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
    public void Payment_requires_server_amount_and_verified_provider_confirmation()
    {
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var payment = new Payment(
            ids[0],
            PaymentPurpose.Membership,
            ids[1],
            ids[2],
            55,
            "bam",
            "membership-attempt-1");

        payment.StartCheckout("cs_test_1", now.AddMinutes(30));

        var mismatch = Assert.Throws<DomainException>(() =>
            payment.Succeed(
                "pi_test_1",
                "evt_test_1",
                54,
                "BAM",
                now.AddMinutes(1)));
        Assert.Equal("payment_confirmation_mismatch", mismatch.Code);

        payment.Succeed(
            "pi_test_1",
            "evt_test_1",
            55,
            "bam",
            now.AddMinutes(1));

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(55, payment.ChargedAmount);
        Assert.Equal("BAM", payment.Currency);
        Assert.Throws<DomainException>(() =>
            payment.Fail("evt_test_2", "late_failure", now.AddMinutes(2)));
    }

    [Fact]
    public void Stripe_event_receipt_is_complete_utc_and_idempotently_processed()
    {
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var receipt = new StripeEventReceipt(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "evt_test_1",
            "cs_test_1",
            "checkout.session.completed",
            now);

        receipt.MarkProcessed(now.AddSeconds(1));
        receipt.MarkProcessed(now.AddSeconds(2));

        Assert.Equal(now.AddSeconds(1), receipt.ProcessedAtUtc);
        Assert.Throws<DomainException>(() =>
            new StripeEventReceipt(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "",
                "cs_test_1",
                "checkout.session.completed",
                now));
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
    public void Pending_membership_activates_only_from_verified_payment()
    {
        var ids = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToArray();
        var approvedAt = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var membership = Membership.CreatePendingPayment(
            ids[0], ids[1], ids[2], ids[3], ids[4],
            "Monthly", 30, 55, "bam", ids[5], approvedAt);

        Assert.Equal(MembershipStatus.PendingPayment, membership.Status);
        Assert.Null(membership.StartsAtUtc);
        Assert.Null(membership.EndsAtUtc);
        Assert.Equal(30, membership.DurationDays);

        membership.ActivateFromPayment(ids[6], approvedAt.AddMinutes(2));

        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.Equal(ids[6], membership.PaymentId);
        Assert.Equal(approvedAt.AddMinutes(2), membership.StartsAtUtc);
        Assert.Equal(approvedAt.AddDays(30).AddMinutes(2), membership.EndsAtUtc);
        var cancellation = Assert.Throws<DomainException>(() =>
            membership.CancelByMember(ids[1], approvedAt.AddDays(1)));
        Assert.Equal("paid_cancellation_not_supported", cancellation.Code);
    }

    [Fact]
    public void Unpaid_pending_membership_can_be_cancelled()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var membership = Membership.CreatePendingPayment(
            ids[0], ids[1], ids[2], ids[3], ids[4],
            "Monthly", 30, 55, "BAM", ids[5], now);

        membership.CancelPendingPayment(ids[1], now.AddMinutes(1));

        Assert.Equal(MembershipStatus.Cancelled, membership.Status);
        Assert.Null(membership.StartsAtUtc);
        Assert.Null(membership.EndsAtUtc);
    }

    [Fact]
    public void Prepaid_reservation_holds_then_confirms_or_expires()
    {
        var ids = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var start = now.AddDays(1);
        var reservation = new AppointmentReservation(
            ids[0], ids[1], ids[2], ids[3], null, ids[4],
            start, 60, 25, "BAM");
        reservation.RequirePayment(now.AddMinutes(15));
        reservation.ConfirmFromPayment(ids[5], now.AddMinutes(2));

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Equal(ids[5], reservation.PaymentId);
        var cancellation = Assert.Throws<DomainException>(() =>
            reservation.CancelByMember(ids[1], now.AddMinutes(3)));
        Assert.Equal("paid_cancellation_not_supported", cancellation.Code);

        var abandoned = new AppointmentReservation(
            ids[0], ids[1], ids[2], ids[3], null, ids[4],
            start, 60, 25, "BAM");
        abandoned.RequirePayment(now.AddMinutes(15));
        Assert.Throws<DomainException>(() =>
            abandoned.ExpireUnpaid(now.AddMinutes(14)));
        abandoned.ExpireUnpaid(now.AddMinutes(15));

        Assert.Equal(ReservationStatus.Cancelled, abandoned.Status);
        Assert.Null(abandoned.CancelledByUserId);
        Assert.Equal("Payment window expired.", abandoned.CancellationReason);
    }

    [Fact]
    public void Pay_in_person_reservation_confirms_for_owner_without_payment()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var reservation = new AppointmentReservation(
            ids[0],
            ids[1],
            ids[2],
            ids[3],
            null,
            ids[4],
            now.AddDays(1),
            60,
            25,
            "BAM");

        var forged = Assert.Throws<DomainException>(() =>
            reservation.ConfirmForPayInPerson(ids[5], now));
        Assert.Equal("reservation_owner_required", forged.Code);

        reservation.ConfirmForPayInPerson(ids[1], now);

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Equal(ids[1], reservation.ConfirmedByUserId);
        Assert.Null(reservation.PaymentId);
        Assert.Null(reservation.PaymentDueAtUtc);
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

    [Fact]
    public void Conversation_tracks_pair_latest_message_and_read_only_close()
    {
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var conversation = new Conversation(
            ids[0],
            ids[1],
            ids[2],
            ids[3],
            now);

        conversation.RecordMessage(now.AddMinutes(1));
        conversation.Close(now.AddMinutes(2));
        conversation.Close(now.AddMinutes(3));

        Assert.Equal(Conversation.MemberTrainerType, conversation.Type);
        Assert.Equal(now.AddMinutes(1), conversation.LastMessageAtUtc);
        Assert.Equal(now.AddMinutes(2), conversation.ClosedAtUtc);
        var closed = Assert.Throws<DomainException>(() =>
            conversation.RecordMessage(now.AddMinutes(4)));
        Assert.Equal("conversation_closed", closed.Code);
        Assert.Throws<DomainException>(() =>
            new Conversation(ids[0], ids[1], ids[2], ids[2], now));
    }

    [Fact]
    public void Conversation_participant_read_state_is_monotonic_and_ends_on_leave()
    {
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var participant = new ConversationParticipant(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            now);

        participant.MarkRead(now.AddMinutes(2));
        participant.MarkRead(now.AddMinutes(1));
        participant.Leave(now.AddMinutes(3));
        participant.Leave(now.AddMinutes(4));

        Assert.Equal(now.AddMinutes(2), participant.LastReadAtUtc);
        Assert.Equal(now.AddMinutes(3), participant.LeftAtUtc);
        var ended = Assert.Throws<DomainException>(() =>
            participant.MarkRead(now.AddMinutes(5)));
        Assert.Equal("conversation_participation_ended", ended.Code);
    }

    [Fact]
    public void Message_requires_trimmed_bounded_text_and_utc()
    {
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var message = new Message(
            ids[0],
            ids[1],
            ids[2],
            ids[3],
            "  Hello  ",
            now);

        Assert.Equal("Hello", message.Text);
        Assert.Equal(ids[3], message.ClientMessageId);
        Assert.Throws<DomainException>(() =>
            new Message(ids[0], ids[1], ids[2], Guid.NewGuid(), " ", now));
        Assert.Throws<DomainException>(() =>
            new Message(
                ids[0],
                ids[1],
                ids[2],
                Guid.NewGuid(),
                new string('x', Message.MaximumTextLength + 1),
                now));
        Assert.Throws<DomainException>(() =>
            new Message(
                ids[0],
                ids[1],
                ids[2],
                Guid.NewGuid(),
                "Hello",
                DateTime.SpecifyKind(now, DateTimeKind.Local)));
    }
}
