using GymLink.Domain.Common;
using GymLink.Domain.Enums;

namespace GymLink.Domain.Payments;

public sealed class Payment : TenantEntity, IConcurrencyTracked
{
    private Payment() { }

    public Payment(
        Guid tenantId,
        PaymentPurpose purpose,
        Guid targetId,
        Guid userId,
        decimal amount,
        string currency,
        string idempotencyKey)
    {
        if (tenantId == Guid.Empty || targetId == Guid.Empty || userId == Guid.Empty)
        {
            throw new DomainException(
                "invalid_payment_target",
                "Payment tenant, target and user are required.");
        }

        if (amount <= 0 || string.IsNullOrWhiteSpace(currency))
        {
            throw new DomainException(
                "invalid_payment_amount",
                "Payment amount and currency must be valid.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new DomainException(
                "idempotency_key_required",
                "A payment idempotency key is required.");
        }

        TenantId = tenantId;
        Purpose = purpose;
        TargetId = targetId;
        UserId = userId;
        Amount = amount;
        Currency = currency.Trim().ToUpperInvariant();
        IdempotencyKey = idempotencyKey.Trim();
    }

    public PaymentPurpose Purpose { get; private set; }
    public Guid TargetId { get; private set; }
    public Guid UserId { get; private set; }
    public decimal Amount { get; private set; }
    public decimal? ChargedAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; } = PaymentStatus.Created;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? ProviderSessionId { get; private set; }
    public string? ProviderIntentId { get; private set; }
    public string? LastProviderEventId { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? FailedAtUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public byte[] RowVersion { get; set; } = [];

    public void StartCheckout(string providerSessionId, DateTime expiresAtUtc)
    {
        EnsureUtc(expiresAtUtc);
        if (Status != PaymentStatus.Created || string.IsNullOrWhiteSpace(providerSessionId))
        {
            throw InvalidTransition(PaymentStatus.Processing);
        }

        ProviderSessionId = providerSessionId.Trim();
        ExpiresAtUtc = expiresAtUtc;
        Status = PaymentStatus.Processing;
    }

    public void Succeed(
        string providerIntentId,
        string providerEventId,
        decimal chargedAmount,
        string currency,
        DateTime completedAtUtc)
    {
        EnsureUtc(completedAtUtc);
        if (Status is not PaymentStatus.Created and
            not PaymentStatus.Processing and
            not PaymentStatus.Failed)
        {
            throw InvalidTransition(PaymentStatus.Succeeded);
        }

        if (string.IsNullOrWhiteSpace(providerIntentId) ||
            string.IsNullOrWhiteSpace(providerEventId) ||
            chargedAmount != Amount ||
            !string.Equals(currency, Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(
                "payment_confirmation_mismatch",
                "The provider payment does not match the server payment record.");
        }

        ProviderIntentId = providerIntentId.Trim();
        LastProviderEventId = providerEventId.Trim();
        ChargedAmount = chargedAmount;
        CompletedAtUtc = completedAtUtc;
        FailedAtUtc = null;
        FailureCode = null;
        Status = PaymentStatus.Succeeded;
    }

    public void Fail(string? providerEventId, string failureCode, DateTime failedAtUtc)
    {
        EnsureUtc(failedAtUtc);
        if (Status is not PaymentStatus.Created and not PaymentStatus.Processing)
        {
            throw InvalidTransition(PaymentStatus.Failed);
        }

        if (string.IsNullOrWhiteSpace(failureCode))
        {
            throw new DomainException("failure_code_required", "A safe failure code is required.");
        }

        LastProviderEventId = string.IsNullOrWhiteSpace(providerEventId)
            ? null
            : providerEventId.Trim();
        FailedAtUtc = failedAtUtc;
        FailureCode = failureCode.Trim();
        Status = PaymentStatus.Failed;
    }

    private DomainException InvalidTransition(PaymentStatus target) =>
        new(
            "invalid_state_transition",
            $"Payment cannot transition from {Status} to {target}.");

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new DomainException("utc_required", "Payment times must use UTC.");
        }
    }
}

public sealed class StripeEventReceipt : TenantEntity
{
    private StripeEventReceipt() { }

    public StripeEventReceipt(
        Guid tenantId,
        Guid paymentId,
        string providerEventId,
        string providerObjectId,
        string eventType,
        DateTime receivedAtUtc)
    {
        if (tenantId == Guid.Empty || paymentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(providerEventId) ||
            string.IsNullOrWhiteSpace(providerObjectId) ||
            string.IsNullOrWhiteSpace(eventType))
        {
            throw new DomainException(
                "invalid_provider_event",
                "A complete provider event receipt is required.");
        }

        if (receivedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException("utc_required", "Provider event time must use UTC.");
        }

        TenantId = tenantId;
        PaymentId = paymentId;
        ProviderEventId = providerEventId.Trim();
        ProviderObjectId = providerObjectId.Trim();
        EventType = eventType.Trim();
        ReceivedAtUtc = receivedAtUtc;
    }

    public Guid PaymentId { get; private set; }
    public string ProviderEventId { get; private set; } = string.Empty;
    public string ProviderObjectId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }

    public void MarkProcessed(DateTime processedAtUtc)
    {
        if (processedAtUtc.Kind != DateTimeKind.Utc || processedAtUtc < ReceivedAtUtc)
        {
            throw new DomainException(
                "invalid_provider_event_time",
                "Provider event processing time must be valid UTC.");
        }

        ProcessedAtUtc ??= processedAtUtc;
    }
}
