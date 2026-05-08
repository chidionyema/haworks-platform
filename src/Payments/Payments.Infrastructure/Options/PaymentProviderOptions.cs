using Haworks.Contracts.Payments;

namespace Haworks.Payments.Infrastructure.Options;

/// <summary>
/// Configuration options for payment providers.
/// </summary>
public sealed class PaymentProviderOptions
{
    public const string SectionName = "PaymentProviders";

    public PaymentProvider Active { get; set; } = PaymentProvider.Stripe;

    public StripeOptions Stripe { get; set; } = new();

    public PayPalOptions PayPal { get; set; } = new();
}
