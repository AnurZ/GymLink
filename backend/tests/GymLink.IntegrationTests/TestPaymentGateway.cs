using System.Collections.Concurrent;
using GymLink.Application.Payments;

namespace GymLink.IntegrationTests;

internal sealed class TestPaymentGateway : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, PaymentGatewaySession> sessions = [];

    public PaymentGatewayCheckoutRequest? LastCheckoutRequest { get; private set; }

    public Task<PaymentGatewaySession> CreateCheckoutAsync(
        PaymentGatewayCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        LastCheckoutRequest = request;
        var sessionId = $"cs_test_{request.PaymentId:N}";
        var session = new PaymentGatewaySession(
            sessionId,
            $"https://checkout.test/{sessionId}",
            "open",
            "unpaid",
            null,
            request.AmountMinor,
            request.Currency,
            DateTime.UtcNow.AddMinutes(30),
            request.PaymentId,
            request.Purpose,
            request.TargetId,
            request.TenantId,
            request.UserId);
        sessions[sessionId] = session;
        return Task.FromResult(session);
    }

    public Task<PaymentGatewaySession?> GetCheckoutAsync(
        string providerSessionId,
        CancellationToken cancellationToken)
    {
        sessions.TryGetValue(providerSessionId, out var session);
        return Task.FromResult(session);
    }

    public Task ExpireCheckoutAsync(
        string providerSessionId,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public PaymentGatewayEvent? ParseWebhook(string payload, string signature)
    {
        if (!sessions.TryGetValue(payload, out var session))
        {
            return null;
        }

        var paid = session with
        {
            Status = "complete",
            PaymentStatus = "paid",
            PaymentIntentId = $"pi_test_{session.PaymentId:N}",
        };
        sessions[payload] = paid;
        return new PaymentGatewayEvent(
            $"evt_test_{session.PaymentId:N}",
            "checkout.session.completed",
            paid,
            DateTime.UtcNow);
    }
}
