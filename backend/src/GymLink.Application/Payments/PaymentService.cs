using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Application.Messaging;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Memberships;
using GymLink.Domain.Payments;
using GymLink.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Payments;

internal sealed class PaymentService(
    IApplicationDbContext dbContext,
    IApplicationTransaction transaction,
    IPaymentGateway gateway,
    IIdentityAccountManager identityAccounts,
    ICurrentUser currentUser,
    ITenantMutationScope tenantMutationScope,
    IOutboxWriter outbox,
    IRequestMetadata requestMetadata,
    IConversationProvisioner conversationProvisioner,
    IConversationRealtimeNotifier conversationNotifier,
    TimeProvider timeProvider) : IPaymentService
{
    private static readonly PaymentStatus[] OpenOrSuccessful =
        [PaymentStatus.Created, PaymentStatus.Processing, PaymentStatus.Succeeded];

    public async Task<CheckoutSessionDto> CreateMembershipPlanCheckoutAsync(
        Guid membershipPlanId,
        CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var membershipId = await transaction.ExecuteSerializableAsync(async ct =>
        {
            var plan = await (
                    from candidate in dbContext.MembershipPlans.IgnoreQueryFilters()
                    join gym in dbContext.Gyms.IgnoreQueryFilters()
                        on new { candidate.TenantId, Id = candidate.GymId }
                        equals new { gym.TenantId, gym.Id }
                    join tenant in dbContext.Tenants
                        on candidate.TenantId equals tenant.Id
                    where candidate.Id == membershipPlanId &&
                          candidate.IsActive &&
                          gym.IsPubliclyVisible &&
                          tenant.Status == TenantStatus.Active
                    select candidate)
                .SingleOrDefaultAsync(ct)
                ?? throw new NotFoundException(
                    "membership_plan_not_found",
                    "Membership plan was not found.");

            var current = await dbContext.Memberships.IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    x => x.TenantId == plan.TenantId &&
                         x.MemberUserId == userId &&
                         x.GymId == plan.GymId &&
                         (x.Status == MembershipStatus.PendingPayment ||
                          x.Status == MembershipStatus.Active ||
                          x.Status == MembershipStatus.Suspended),
                    ct);
            if (current is not null)
            {
                if (current.Status == MembershipStatus.PendingPayment &&
                    current.MembershipPlanId == plan.Id)
                {
                    return current.Id;
                }

                throw new ConflictException(
                    "current_membership_exists",
                    "A current membership already exists for this gym.");
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var request = await dbContext.MembershipRequests.IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    x => x.TenantId == plan.TenantId &&
                         x.MemberUserId == userId &&
                         x.GymId == plan.GymId &&
                         x.Status == MembershipRequestStatus.Pending,
                    ct);
            if (request is not null && request.MembershipPlanId != plan.Id)
            {
                request.Cancel(userId, now);
                request = null;
            }

            var isNewRequest = request is null;
            request ??= new MembershipRequest
            {
                TenantId = plan.TenantId,
                MemberUserId = userId,
                GymId = plan.GymId,
                MembershipPlanId = plan.Id,
                RequestedAtUtc = now,
            };
            request.Approve(userId, now);
            var membership = Membership.CreatePendingPayment(
                plan.TenantId,
                userId,
                plan.GymId,
                plan.Id,
                request.Id,
                plan.Name,
                plan.DurationDays,
                plan.Price,
                plan.Currency,
                userId,
                now);
            using (tenantMutationScope.Begin(plan.TenantId))
            {
                if (isNewRequest)
                {
                    dbContext.MembershipRequests.Add(request);
                }
                dbContext.Memberships.Add(membership);
                await dbContext.SaveChangesAsync(ct);
            }

            return membership.Id;
        }, cancellationToken);

        return await CreateMembershipCheckoutAsync(membershipId, cancellationToken);
    }

    public Task<CheckoutSessionDto> CreateMembershipCheckoutAsync(
        Guid membershipId,
        CancellationToken cancellationToken) =>
        CreateCheckoutAsync(PaymentPurpose.Membership, membershipId, cancellationToken);

    public Task<CheckoutSessionDto> CreateReservationCheckoutAsync(
        Guid reservationId,
        CancellationToken cancellationToken) =>
        CreateCheckoutAsync(PaymentPurpose.TrainerReservation, reservationId, cancellationToken);

    public async Task<PaymentDto> GetMineAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var payment = await dbContext.Payments.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == paymentId && x.UserId == userId, cancellationToken)
            ?? throw PaymentNotFound();
        return Map(payment);
    }

    public async Task HandleWebhookAsync(
        string payload,
        string signature,
        CancellationToken cancellationToken)
    {
        var providerEvent = gateway.ParseWebhook(payload, signature);
        if (providerEvent is not null)
        {
            await ApplyProviderEventAsync(providerEvent, cancellationToken);
        }
    }

    public async Task<Guid?> ReconcileReturnAsync(
        string? providerSessionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerSessionId))
        {
            return null;
        }

        PaymentGatewaySession? session;
        try
        {
            session = await gateway.GetCheckoutAsync(
                providerSessionId.Trim(),
                cancellationToken);
        }
        catch (ExternalServiceUnavailableException)
        {
            return await dbContext.Payments.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.ProviderSessionId == providerSessionId.Trim())
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);
        }
        if (session is null)
        {
            return null;
        }

        var type = IsPaid(session)
            ? "gymlink.checkout.reconciled.paid"
            : session.Status == "expired"
                ? "gymlink.checkout.reconciled.expired"
                : null;
        if (type is null)
        {
            return session.PaymentId;
        }

        await ApplyProviderEventAsync(
            new PaymentGatewayEvent(
                $"reconcile:{session.SessionId}:{type}",
                type,
                session,
                timeProvider.GetUtcNow().UtcDateTime),
            cancellationToken);
        return session.PaymentId;
    }

    private async Task<CheckoutSessionDto> CreateCheckoutAsync(
        PaymentPurpose purpose,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteSerializableAsync(
                ct => CreateCheckoutCoreAsync(purpose, targetId, ct),
                cancellationToken);
        }
        catch (Exception exception) when (IsCheckoutRace(exception))
        {
            dbContext.ClearTrackedChanges();
            return await transaction.ExecuteSerializableAsync(
                ct => CreateCheckoutCoreAsync(purpose, targetId, ct),
                cancellationToken);
        }
    }

    private async Task<CheckoutSessionDto> CreateCheckoutCoreAsync(
        PaymentPurpose purpose,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var quote = await LoadQuoteAsync(purpose, targetId, userId, cancellationToken);
        var account = await identityAccounts.FindByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("user_not_found", "User account was not found.");
        var payment = await dbContext.Payments.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.TenantId == quote.TenantId &&
                     x.Purpose == purpose &&
                     x.TargetId == targetId &&
                     OpenOrSuccessful.Contains(x.Status),
                cancellationToken);

        if (payment?.Status == PaymentStatus.Succeeded)
        {
            throw new ConflictException("payment_already_completed", "This purchase is already paid.");
        }

        if (payment?.Status == PaymentStatus.Processing &&
            payment.ProviderSessionId is not null &&
            payment.ExpiresAtUtc > timeProvider.GetUtcNow().UtcDateTime)
        {
            var open = await gateway.GetCheckoutAsync(payment.ProviderSessionId, cancellationToken);
            if (open?.CheckoutUrl is not null && open.Status == "open")
            {
                return new(payment.Id, open.CheckoutUrl, payment.Status,
                    payment.ExpiresAtUtc ?? open.ExpiresAtUtc);
            }
        }

        var isNew = payment is null;
        payment ??= new Payment(
                quote.TenantId,
                purpose,
                targetId,
                userId,
                quote.Amount,
                quote.Currency,
                $"checkout:{purpose}:{targetId}:{Guid.NewGuid():N}");

        if (payment.Status != PaymentStatus.Created)
        {
            throw new ConflictException(
                "checkout_not_reusable",
                "The previous Checkout is no longer open. Refresh and try again.");
        }

        if (isNew)
        {
            using (tenantMutationScope.Begin(quote.TenantId))
            {
                dbContext.Payments.Add(payment);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        var providerSession = await gateway.CreateCheckoutAsync(
            new PaymentGatewayCheckoutRequest(
                payment.Id,
                purpose,
                targetId,
                quote.TenantId,
                userId,
                ToMinorUnits(quote.Amount, quote.Currency),
                quote.Currency,
                quote.Description,
                account.Email,
                payment.IdempotencyKey),
            cancellationToken);
        if (providerSession.CheckoutUrl is null)
        {
            throw new ExternalServiceUnavailableException(
                "payment_checkout_unavailable",
                "The payment provider did not return a Checkout URL.");
        }

        var localExpiry = quote.DeadlineUtc ??
            timeProvider.GetUtcNow().UtcDateTime.AddMinutes(15);
        using (tenantMutationScope.Begin(quote.TenantId))
        {
            payment.StartCheckout(providerSession.SessionId, localExpiry);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new(payment.Id, providerSession.CheckoutUrl, payment.Status, localExpiry);
    }

    private async Task<PaymentQuote> LoadQuoteAsync(
        PaymentPurpose purpose,
        Guid targetId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (purpose == PaymentPurpose.Membership)
        {
            var membership = await dbContext.Memberships.IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Id == targetId && x.MemberUserId == userId,
                    cancellationToken)
                ?? throw new NotFoundException("membership_not_found", "Membership was not found.");
            if (membership.Status != MembershipStatus.PendingPayment)
            {
                throw new ConflictException(
                    "membership_not_awaiting_payment",
                    "This membership is not awaiting payment.");
            }

            return Quote(
                membership.TenantId,
                membership.Price,
                membership.Currency,
                $"GymLink membership — {membership.PlanName}",
                null);
        }

        var reservation = await dbContext.AppointmentReservations.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == targetId && x.MemberUserId == userId,
                cancellationToken)
            ?? throw new NotFoundException("reservation_not_found", "Reservation was not found.");
        if (reservation.Status != ReservationStatus.Pending ||
            !reservation.PaymentDueAtUtc.HasValue)
        {
            throw new ConflictException(
                "reservation_not_awaiting_payment",
                "This reservation is not awaiting payment.");
        }

        if (reservation.PaymentDueAtUtc <= timeProvider.GetUtcNow().UtcDateTime)
        {
            throw new ConflictException(
                "payment_window_expired",
                "The reservation payment window has expired.");
        }

        return Quote(
            reservation.TenantId,
            reservation.Price,
            reservation.Currency,
            "GymLink trainer reservation",
            reservation.PaymentDueAtUtc);
    }

    private static PaymentQuote Quote(
        Guid tenantId,
        decimal amount,
        string currency,
        string description,
        DateTime? deadlineUtc)
    {
        if (amount <= 0 || !string.Equals(currency, "BAM", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                "unsupported_payment_quote",
                "Online purchases require a positive server-owned BAM price.");
        }

        return new(tenantId, amount, "BAM", description, deadlineUtc);
    }

    private async Task ApplyProviderEventAsync(
        PaymentGatewayEvent providerEvent,
        CancellationToken cancellationToken)
    {
        var session = providerEvent.Session;
        if (!session.PaymentId.HasValue)
        {
            throw new ApplicationRuleException(
                "invalid_payment_metadata",
                "The payment event metadata is invalid.");
        }

        ConversationProvisioningResult? provisionedConversation = null;
        await transaction.ExecuteSerializableAsync(async ct =>
        {
            if (await dbContext.StripeEventReceipts.IgnoreQueryFilters().AnyAsync(
                    x => x.ProviderEventId == providerEvent.EventId ||
                         (x.ProviderObjectId == session.SessionId &&
                          x.EventType == providerEvent.EventType),
                    ct))
            {
                return true;
            }

            var payment = await dbContext.Payments.IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    x => x.Id == session.PaymentId &&
                         x.ProviderSessionId == session.SessionId,
                    ct)
                ?? throw PaymentNotFound();
            ValidateProviderSession(payment, session);

            var now = timeProvider.GetUtcNow().UtcDateTime;
            using (tenantMutationScope.Begin(payment.TenantId))
            {
                var receipt = new StripeEventReceipt(
                    payment.TenantId,
                    payment.Id,
                    providerEvent.EventId,
                    session.SessionId,
                    providerEvent.EventType,
                    now);
                dbContext.StripeEventReceipts.Add(receipt);
                await dbContext.SaveChangesAsync(ct);

                if (IsPaid(session))
                {
                    if (payment.Status != PaymentStatus.Succeeded)
                    {
                        payment.Succeed(
                            session.PaymentIntentId!,
                            providerEvent.EventId,
                            FromMinorUnits(session.AmountTotal),
                            session.Currency,
                            now);
                        provisionedConversation =
                            await ActivateTargetAsync(payment, now, ct);
                        AddPaidNotification(payment, now);
                    }
                }
                else if (IsExpired(providerEvent, session) &&
                         payment.Status is PaymentStatus.Created or PaymentStatus.Processing)
                {
                    payment.Fail(providerEvent.EventId, "checkout_expired", now);
                    await ExpireReservationAsync(payment, now, ct);
                }

                receipt.MarkProcessed(now);
                await dbContext.SaveChangesAsync(ct);
            }

            return true;
        }, cancellationToken);
        if (provisionedConversation is { Created: true })
        {
            await conversationNotifier.ConversationAvailableAsync(
                provisionedConversation,
                CancellationToken.None);
        }
    }

    private async Task<ConversationProvisioningResult?> ActivateTargetAsync(
        Payment payment,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (payment.Purpose == PaymentPurpose.Membership)
        {
            var membership = await dbContext.Memberships.IgnoreQueryFilters()
                .SingleAsync(
                    x => x.Id == payment.TargetId &&
                         x.TenantId == payment.TenantId &&
                         x.MemberUserId == payment.UserId,
                    cancellationToken);
            if (membership.Status == MembershipStatus.PendingPayment)
            {
                membership.ActivateFromPayment(payment.Id, now);
                await ActivateMemberAssignmentAsync(payment, now, cancellationToken);
            }

            return null;
        }

        var reservation = await dbContext.AppointmentReservations.IgnoreQueryFilters()
            .SingleAsync(
                x => x.Id == payment.TargetId &&
                     x.TenantId == payment.TenantId &&
                     x.MemberUserId == payment.UserId,
                cancellationToken);
        if (reservation.Status == ReservationStatus.Pending)
        {
            reservation.ConfirmFromPayment(payment.Id, now);
            return await conversationProvisioner
                .EnsureForConfirmedReservationAsync(reservation, cancellationToken);
        }

        return null;
    }

    private async Task ActivateMemberAssignmentAsync(
        Payment payment,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var assignment = await dbContext.UserGymAssignments.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.TenantId == payment.TenantId &&
                     x.UserId == payment.UserId &&
                     x.Role == RoleNames.Member,
                cancellationToken);
        if (assignment is null)
        {
            dbContext.UserGymAssignments.Add(new UserGymAssignment
            {
                TenantId = payment.TenantId,
                UserId = payment.UserId,
                Role = RoleNames.Member,
                Status = AssignmentStatus.Active,
                StartsAtUtc = now,
                Reason = "Membership payment confirmed.",
            });
            return;
        }

        assignment.Status = AssignmentStatus.Active;
        assignment.StartsAtUtc = now;
        assignment.EndsAtUtc = null;
        assignment.ApprovedByUserId = null;
        assignment.Reason = "Membership payment confirmed.";
    }

    private async Task ExpireReservationAsync(
        Payment payment,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (payment.Purpose != PaymentPurpose.TrainerReservation)
        {
            return;
        }

        var reservation = await dbContext.AppointmentReservations.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == payment.TargetId, cancellationToken);
        if (reservation.Status == ReservationStatus.Pending &&
            reservation.PaymentDueAtUtc <= now)
        {
            reservation.ExpireUnpaid(now);
            if (reservation.AvailabilitySlotId.HasValue)
            {
                var slot = await dbContext.TrainerAvailabilitySlots.IgnoreQueryFilters()
                    .SingleAsync(
                        x => x.Id == reservation.AvailabilitySlotId.Value,
                        cancellationToken);
                slot.Release();
            }
            outbox.AddNotification(new(
                payment.UserId,
                payment.TenantId,
                "payment",
                "Rezervacija je istekla",
                "Termin je oslobođen jer plaćanje nije završeno u roku od 15 minuta.",
                "reservation",
                reservation.Id,
                now,
                requestMetadata.CorrelationId));
        }
    }

    private void AddPaidNotification(Payment payment, DateTime now) =>
        outbox.AddNotification(new(
            payment.UserId,
            payment.TenantId,
            "payment",
            "Plaćeno",
            payment.Purpose == PaymentPurpose.Membership
                ? "Članarina je uspješno plaćena i aktivirana."
                : "Rezervacija je uspješno plaćena i potvrđena.",
            payment.Purpose == PaymentPurpose.Membership ? "membership" : "reservation",
            payment.TargetId,
            now,
            requestMetadata.CorrelationId));

    private static void ValidateProviderSession(Payment payment, PaymentGatewaySession session)
    {
        if (session.PaymentId != payment.Id ||
            session.Purpose != payment.Purpose ||
            session.TargetId != payment.TargetId ||
            session.TenantId != payment.TenantId ||
            session.UserId != payment.UserId ||
            session.AmountTotal != ToMinorUnits(payment.Amount, payment.Currency) ||
            !string.Equals(session.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationRuleException(
                "payment_confirmation_mismatch",
                "The provider payment does not match the server payment record.");
        }
    }

    private static bool IsPaid(PaymentGatewaySession session) =>
        session.Status == "complete" &&
        session.PaymentStatus == "paid" &&
        !string.IsNullOrWhiteSpace(session.PaymentIntentId);

    private static bool IsExpired(
        PaymentGatewayEvent providerEvent,
        PaymentGatewaySession session) =>
        providerEvent.EventType.EndsWith("expired", StringComparison.Ordinal) ||
        session.Status == "expired";

    internal static long ToMinorUnits(decimal amount, string currency)
    {
        if (!string.Equals(currency, "BAM", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationRuleException(
                "unsupported_currency",
                "Only BAM payments are supported.");
        }

        var minor = amount * 100m;
        if (minor != decimal.Truncate(minor) || minor > long.MaxValue)
        {
            throw new ApplicationRuleException(
                "invalid_payment_amount",
                "The payment amount cannot be represented in minor units.");
        }

        return decimal.ToInt64(minor);
    }

    private static decimal FromMinorUnits(long amount) => amount / 100m;

    private static bool IsCheckoutRace(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateException)
            {
                return true;
            }

            if (current.GetType().FullName == "Microsoft.Data.SqlClient.SqlException")
            {
                var number = current.GetType().GetProperty("Number")?.GetValue(current);
                if (number is 1205 or 2601 or 2627)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private Guid RequireUser() =>
        currentUser.IsAuthenticated && currentUser.UserId.HasValue
            ? currentUser.UserId.Value
            : throw new AuthorizationDeniedException();

    private static PaymentDto Map(Payment payment) =>
        new(
            payment.Id,
            payment.Purpose,
            payment.TargetId,
            payment.Amount,
            payment.Currency,
            payment.Status,
            payment.ExpiresAtUtc,
            payment.CompletedAtUtc,
            payment.FailureCode,
            payment.Status == PaymentStatus.Succeeded);

    private static NotFoundException PaymentNotFound() =>
        new("payment_not_found", "Payment was not found.");

    private sealed record PaymentQuote(
        Guid TenantId,
        decimal Amount,
        string Currency,
        string Description,
        DateTime? DeadlineUtc);
}

internal sealed class PaymentReconciliationService(
    IApplicationDbContext dbContext,
    IPaymentGateway gateway,
    IPaymentService paymentService,
    TimeProvider timeProvider) : IPaymentReconciliationService
{
    public async Task<int> ReconcileDueAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var due = await dbContext.Payments.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Status == PaymentStatus.Processing &&
                        x.ExpiresAtUtc.HasValue &&
                        x.ExpiresAtUtc <= now &&
                        x.ProviderSessionId != null)
            .OrderBy(x => x.ExpiresAtUtc)
            .Select(x => x.ProviderSessionId!)
            .Take(100)
            .ToListAsync(cancellationToken);
        var processed = 0;
        foreach (var sessionId in due)
        {
            var session = await gateway.GetCheckoutAsync(sessionId, cancellationToken);
            if (session is null)
            {
                continue;
            }

            if (session.Status == "open" && session.PaymentStatus != "paid")
            {
                await gateway.ExpireCheckoutAsync(sessionId, cancellationToken);
            }

            _ = await paymentService.ReconcileReturnAsync(sessionId, cancellationToken);
            processed++;
        }

        return processed;
    }
}
