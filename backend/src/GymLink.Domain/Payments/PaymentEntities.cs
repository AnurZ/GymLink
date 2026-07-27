using GymLink.Domain.Common;
using GymLink.Domain.Enums;

namespace GymLink.Domain.Payments;

public sealed class Payment : TenantEntity, IConcurrencyTracked
{
    public PaymentPurpose Purpose { get; set; }
    public Guid TargetId { get; set; }
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public decimal? ChargedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public PaymentStatus Status { get; set; } = PaymentStatus.Created;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? ProviderIntentId { get; set; }
    public string? LastProviderEventId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class Refund : TenantEntity, IConcurrencyTracked
{
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public RefundStatus Status { get; set; } = RefundStatus.Created;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? ProviderRefundId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public static void EnsureTotalDoesNotExceedChargedAmount(
        decimal chargedAmount,
        decimal alreadyRefunded,
        decimal requestedAmount)
    {
        if (requestedAmount <= 0 || alreadyRefunded + requestedAmount > chargedAmount)
        {
            throw new DomainException(
                "invalid_refund_amount",
                "Refund amount must be positive and cannot exceed the charged amount.");
        }
    }
}
