namespace Haworks.Payments.Application.Interfaces;

public interface IWebhookRouter
{
    IWebhookProcessor? GetProcessor(PaymentProvider provider);
    IEnumerable<PaymentProvider> GetRegisteredProviders();
    Task RouteAsync(string providerHeader, string payload, IHeaderDictionary headers, CancellationToken ct = default);
}
