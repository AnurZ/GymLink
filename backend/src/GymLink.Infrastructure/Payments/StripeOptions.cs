namespace GymLink.Infrastructure.Payments;

public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    public bool Enabled { get; init; }
    public string SecretKey { get; init; } = string.Empty;
    public string WebhookSecret { get; init; } = string.Empty;
    public string SuccessUrl { get; init; } = string.Empty;
    public string CancelUrl { get; init; } = string.Empty;
}
