using GymLink.Application.Common;
using GymLink.Application.Payments;
using GymLink.Domain.Enums;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace GymLink.Infrastructure.Payments;

internal sealed class StripePaymentGateway(IOptions<StripeOptions> options) : IPaymentGateway
{
    private readonly StripeOptions settings = options.Value;

    public async Task<PaymentGatewaySession> CreateCheckoutAsync(
        PaymentGatewayCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        try
        {
            var service = new SessionService(new StripeClient(settings.SecretKey));
            var session = await service.CreateAsync(
                new SessionCreateOptions
                {
                    Mode = "payment",
                    SuccessUrl = AppendSessionPlaceholder(settings.SuccessUrl),
                    CancelUrl = settings.CancelUrl,
                    ClientReferenceId = request.PaymentId.ToString(),
                    CustomerEmail = request.CustomerEmail,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(31),
                    PaymentMethodTypes = ["card"],
                    Metadata = new Dictionary<string, string>
                    {
                        ["paymentId"] = request.PaymentId.ToString(),
                        ["purpose"] = request.Purpose.ToString(),
                        ["targetId"] = request.TargetId.ToString(),
                        ["tenantId"] = request.TenantId.ToString(),
                        ["userId"] = request.UserId.ToString(),
                    },
                    PaymentIntentData = new SessionPaymentIntentDataOptions
                    {
                        Metadata = new Dictionary<string, string>
                        {
                            ["paymentId"] = request.PaymentId.ToString(),
                        },
                    },
                    LineItems =
                    [
                        new SessionLineItemOptions
                        {
                            Quantity = 1,
                            PriceData = new SessionLineItemPriceDataOptions
                            {
                                Currency = request.Currency.ToLowerInvariant(),
                                UnitAmount = request.AmountMinor,
                                ProductData = new SessionLineItemPriceDataProductDataOptions
                                {
                                    Name = request.Description,
                                },
                            },
                        },
                    ],
                },
                new RequestOptions { IdempotencyKey = request.IdempotencyKey },
                cancellationToken);
            return Map(session);
        }
        catch (StripeException exception)
        {
            throw ProviderUnavailable(exception);
        }
    }

    public async Task<PaymentGatewaySession?> GetCheckoutAsync(
        string providerSessionId,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        try
        {
            var service = new SessionService(new StripeClient(settings.SecretKey));
            return Map(await service.GetAsync(providerSessionId, cancellationToken: cancellationToken));
        }
        catch (StripeException exception) when (exception.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (StripeException exception)
        {
            throw ProviderUnavailable(exception);
        }
    }

    public async Task ExpireCheckoutAsync(
        string providerSessionId,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        try
        {
            var service = new SessionService(new StripeClient(settings.SecretKey));
            await service.ExpireAsync(providerSessionId, cancellationToken: cancellationToken);
        }
        catch (StripeException exception)
        {
            throw ProviderUnavailable(exception);
        }
    }

    public PaymentGatewayEvent? ParseWebhook(string payload, string signature)
    {
        EnsureEnabled();
        if (string.IsNullOrWhiteSpace(signature))
        {
            throw new ApplicationRuleException(
                "invalid_payment_signature",
                "The payment signature is invalid.");
        }

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                payload,
                signature,
                settings.WebhookSecret);
        }
        catch (StripeException)
        {
            throw new ApplicationRuleException(
                "invalid_payment_signature",
                "The payment signature is invalid.");
        }

        if (stripeEvent.Type is not "checkout.session.completed" and
            not "checkout.session.expired")
        {
            return null;
        }

        if (stripeEvent.Data.Object is not Session session)
        {
            throw new ApplicationRuleException(
                "invalid_payment_event",
                "The payment event payload is invalid.");
        }

        return new PaymentGatewayEvent(
            stripeEvent.Id,
            stripeEvent.Type,
            Map(session),
            DateTime.SpecifyKind(stripeEvent.Created, DateTimeKind.Utc));
    }

    private static PaymentGatewaySession Map(Session session)
    {
        var paymentId = ParseGuid(session.Metadata, "paymentId");
        var targetId = ParseGuid(session.Metadata, "targetId");
        var tenantId = ParseGuid(session.Metadata, "tenantId");
        var userId = ParseGuid(session.Metadata, "userId");
        PaymentPurpose? purpose = null;
        if (session.Metadata.TryGetValue("purpose", out var rawPurpose) &&
            Enum.TryParse<PaymentPurpose>(rawPurpose, out var parsedPurpose))
        {
            purpose = parsedPurpose;
        }

        return new PaymentGatewaySession(
            session.Id,
            session.Url,
            session.Status ?? string.Empty,
            session.PaymentStatus ?? string.Empty,
            session.PaymentIntentId,
            session.AmountTotal ?? 0,
            session.Currency?.ToUpperInvariant() ?? string.Empty,
            DateTime.SpecifyKind(session.ExpiresAt, DateTimeKind.Utc),
            paymentId,
            purpose,
            targetId,
            tenantId,
            userId);
    }

    private static Guid? ParseGuid(Dictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var raw) && Guid.TryParse(raw, out var parsed)
            ? parsed
            : null;

    private static string AppendSessionPlaceholder(string successUrl)
    {
        var separator = successUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{successUrl}{separator}session_id={{CHECKOUT_SESSION_ID}}";
    }

    private void EnsureEnabled()
    {
        if (!settings.Enabled)
        {
            throw new ApplicationRuleException(
                "payment_provider_unavailable",
                "Online payment is not configured.");
        }
    }

    private static ExternalServiceUnavailableException ProviderUnavailable(
        StripeException exception) =>
        new(
            "payment_provider_unavailable",
            "The payment provider is temporarily unavailable.",
            exception);
}
