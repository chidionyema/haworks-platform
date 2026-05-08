using Haworks.Payments.Application.Interfaces;
using Haworks.Contracts.Payments;
using Haworks.Payments.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Haworks.Payments.Infrastructure;

/// <summary>
/// Main facade for payment operations.
/// Provides a single entry point for all payment-related functionality,
/// delegating to the configured provider's implementations.
/// </summary>
internal sealed class PaymentGateway(
    IServiceProvider serviceProvider,
    IOptions<PaymentProviderOptions> options) : IPaymentGateway
{
    public PaymentProvider ActiveProvider { get; } = options.Value.Active;

    public ICheckoutSessionService Checkout => ActiveProvider switch
    {
        PaymentProvider.Stripe => serviceProvider.GetRequiredService<Stripe.StripeCheckoutSessionService>(),
        PaymentProvider.PayPal => serviceProvider.GetRequiredService<PayPal.PayPalCheckoutService>(),
        _ => throw new NotSupportedException($"Checkout not supported for {ActiveProvider}")
    };

    public ISubscriptionManager Subscriptions => ActiveProvider switch
    {
        PaymentProvider.Stripe => serviceProvider.GetRequiredService<Stripe.StripeSubscriptionManager>(),
        PaymentProvider.PayPal => serviceProvider.GetRequiredService<PayPal.PayPalSubscriptionManager>(),
        _ => throw new NotSupportedException($"Subscriptions not supported for {ActiveProvider}")
    };

    public IRefundService Refunds => ActiveProvider switch
    {
        PaymentProvider.Stripe => serviceProvider.GetRequiredService<Stripe.StripeRefundService>(),
        PaymentProvider.PayPal => serviceProvider.GetRequiredService<PayPal.PayPalRefundService>(),
        _ => throw new NotSupportedException($"Refunds not supported for {ActiveProvider}")
    };

    public IWebhookProcessor Webhooks => serviceProvider.GetRequiredService<Webhooks.WebhookRouter>().GetProcessor(ActiveProvider)
        ?? throw new InvalidOperationException($"No webhook processor found for {ActiveProvider}");

    public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new ProviderHealthStatus { IsHealthy = true, Provider = ActiveProvider });
    }
}
