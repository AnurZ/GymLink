using GymLink.Domain.Enums;

namespace GymLink.Application.Payments;

public sealed record CreateMembershipPlanCheckoutRequest(Guid MembershipPlanId);

public sealed record CreateManualMembershipPlanPaymentRequest(Guid MembershipPlanId);

public sealed record CheckoutSessionDto(
    Guid PaymentId,
    string CheckoutUrl,
    PaymentStatus Status,
    DateTime ExpiresAtUtc);

public sealed record PaymentDto(
    Guid Id,
    PaymentPurpose Purpose,
    Guid TargetId,
    decimal Amount,
    string Currency,
    PaymentStatus Status,
    DateTime? ExpiresAtUtc,
    DateTime? CompletedAtUtc,
    string? FailureCode,
    bool IsPaid);

public sealed record PaymentGatewayCheckoutRequest(
    Guid PaymentId,
    PaymentPurpose Purpose,
    Guid TargetId,
    Guid TenantId,
    Guid UserId,
    long AmountMinor,
    string Currency,
    string Description,
    string CustomerEmail,
    string IdempotencyKey);

public sealed record PaymentGatewaySession(
    string SessionId,
    string? CheckoutUrl,
    string Status,
    string PaymentStatus,
    string? PaymentIntentId,
    long AmountTotal,
    string Currency,
    DateTime ExpiresAtUtc,
    Guid? PaymentId,
    PaymentPurpose? Purpose,
    Guid? TargetId,
    Guid? TenantId,
    Guid? UserId);

public sealed record PaymentGatewayEvent(
    string EventId,
    string EventType,
    PaymentGatewaySession Session,
    DateTime OccurredAtUtc);

public interface IPaymentGateway
{
    Task<PaymentGatewaySession> CreateCheckoutAsync(
        PaymentGatewayCheckoutRequest request,
        CancellationToken cancellationToken);

    Task<PaymentGatewaySession?> GetCheckoutAsync(
        string providerSessionId,
        CancellationToken cancellationToken);

    Task ExpireCheckoutAsync(
        string providerSessionId,
        CancellationToken cancellationToken);

    PaymentGatewayEvent? ParseWebhook(string payload, string signature);
}

public interface IPaymentService
{
    Task<CheckoutSessionDto> CreateMembershipPlanCheckoutAsync(
        Guid membershipPlanId,
        CancellationToken cancellationToken);

    Task<CheckoutSessionDto> CreateMembershipCheckoutAsync(
        Guid membershipId,
        CancellationToken cancellationToken);

    Task<CheckoutSessionDto> CreateReservationCheckoutAsync(
        Guid reservationId,
        CancellationToken cancellationToken);

    Task<PaymentDto> CompleteManualMembershipPlanPaymentAsync(
        Guid membershipPlanId,
        CancellationToken cancellationToken);

    Task<PaymentDto> CompleteManualMembershipPaymentAsync(
        Guid membershipId,
        CancellationToken cancellationToken);

    Task<PaymentDto> CompleteManualReservationPaymentAsync(
        Guid reservationId,
        CancellationToken cancellationToken);

    Task<PaymentDto> GetMineAsync(Guid paymentId, CancellationToken cancellationToken);

    Task HandleWebhookAsync(
        string payload,
        string signature,
        CancellationToken cancellationToken);

    Task<Guid?> ReconcileReturnAsync(
        string? providerSessionId,
        CancellationToken cancellationToken);
}

public interface IFakePaymentAvailability
{
    bool Enabled { get; }
}

public interface IPaymentReconciliationService
{
    Task<int> ReconcileDueAsync(CancellationToken cancellationToken);
}
