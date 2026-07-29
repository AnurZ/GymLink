using GymLink.Application.Common;
using GymLink.Infrastructure.Payments;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace GymLink.IntegrationTests;

public sealed class StripePaymentGatewayTests
{
    private static readonly StripeOptions Settings = new()
    {
        Enabled = true,
        SecretKey = "sk_test_not_used_by_signature_tests",
        WebhookSecret = "whsec_signature_test_secret",
        SuccessUrl = "https://localhost/payments/stripe/success",
        CancelUrl = "https://localhost/payments/stripe/cancel",
    };

    [Theory]
    [InlineData("")]
    [InlineData("t=1,v1=forged")]
    public void Webhook_rejects_missing_or_invalid_signature(string signature)
    {
        var gateway = new StripePaymentGateway(Options.Create(Settings));

        var exception = Assert.Throws<ApplicationRuleException>(() =>
            gateway.ParseWebhook("{}", signature));

        Assert.Equal("invalid_payment_signature", exception.Code);
    }

    [Fact]
    public void Webhook_rejects_a_stale_valid_signature()
    {
        const string payload = "{}";
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Settings.WebhookSecret));
        var hash = Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}")))
            .ToLowerInvariant();
        var gateway = new StripePaymentGateway(Options.Create(Settings));

        var exception = Assert.Throws<ApplicationRuleException>(() =>
            gateway.ParseWebhook(payload, $"t={timestamp},v1={hash}"));

        Assert.Equal("invalid_payment_signature", exception.Code);
    }
}
