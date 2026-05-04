namespace Haworks.Payments.Infrastructure.Options;

public sealed class StripeOptions
{
    public const string SectionName = "Payments:Stripe";

    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string MetadataSignatureSecret { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}
