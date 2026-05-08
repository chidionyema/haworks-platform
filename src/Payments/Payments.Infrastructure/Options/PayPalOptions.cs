using System.ComponentModel.DataAnnotations;

namespace Haworks.Payments.Infrastructure.Options;

public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    [Required]
    [Url]
    public string BaseUrl { get; set; } = "https://api-m.sandbox.paypal.com";

    [Required]
    public string WebhookId { get; set; } = string.Empty;
}
