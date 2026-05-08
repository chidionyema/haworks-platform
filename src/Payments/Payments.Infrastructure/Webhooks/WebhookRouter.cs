using Haworks.Payments.Application.Interfaces;
using Haworks.Contracts.Payments;
using Microsoft.Extensions.Logging;

namespace Haworks.Payments.Infrastructure.Webhooks;

internal sealed class WebhookRouter(
    IEnumerable<IWebhookProcessor> processors,
    ILogger<WebhookRouter> logger) : IWebhookRouter
{
    private readonly Dictionary<PaymentProvider, IWebhookProcessor> _processors = 
        processors.ToDictionary(p => p.Provider);

    public IWebhookProcessor? GetProcessor(PaymentProvider provider)
    {
        if (_processors.TryGetValue(provider, out var processor)) return processor;
        logger.LogWarning("No webhook processor registered for provider {Provider}", provider);
        return null;
    }

    public IEnumerable<PaymentProvider> GetRegisteredProviders() => _processors.Keys;

    public async Task RouteAsync(
        string providerHeader, 
        string payload, 
        IHeaderDictionary headers, 
        CancellationToken ct = default)
    {
        var provider = DetermineProvider(providerHeader, headers);
        var processor = GetProcessor(provider);
        if (processor == null) return;

        var signature = provider == PaymentProvider.Stripe 
            ? headers["Stripe-Signature"].FirstOrDefault() 
            : string.Empty;

        var validationResult = await processor.ValidateAndParseAsync(payload, signature ?? string.Empty, ct);
        if (validationResult.IsValid && validationResult.Event != null)
        {
            await processor.ProcessEventAsync(validationResult.Event, ct);
        }
    }

    private PaymentProvider DetermineProvider(string header, IHeaderDictionary headers)
    {
        if (header.Equals("stripe", StringComparison.OrdinalIgnoreCase)) return PaymentProvider.Stripe;
        if (header.Equals("paypal", StringComparison.OrdinalIgnoreCase)) return PaymentProvider.PayPal;
        return PaymentProvider.None;
    }
}
