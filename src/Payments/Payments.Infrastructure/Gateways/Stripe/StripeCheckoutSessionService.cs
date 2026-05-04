using Haworks.Payments.Application.Interfaces;
using Haworks.Payments.Domain;
using Haworks.BuildingBlocks.Resilience;
using Polly;
using Stripe;
using Stripe.Checkout;
using Microsoft.Extensions.Logging;

namespace Haworks.Payments.Infrastructure.Gateways.Stripe;

internal sealed class StripeCheckoutSessionService(
    IStripeClientFactory clientFactory,
    IResiliencePolicyFactory resiliencePolicyFactory,
    ILogger<StripeCheckoutSessionService> logger) : ICheckoutSessionService
{
    // BuildingBlocks.Resilience exposes a Stripe-specific preset (1s
    // initial delay, longer than the default 200ms because Stripe's API
    // edges into hundreds of ms even on warm paths) — use it instead of
    // the non-existent ResilienceOptions.Payments.
    private readonly IAsyncPolicy _resiliencePolicy =
        resiliencePolicyFactory.CreatePolicy(ResilienceOptions.Stripe);

    // Logger reserved for the next iteration that adds breadcrumb logs at
    // session-create entry / success / failure boundaries.
    private readonly ILogger<StripeCheckoutSessionService> _logger = logger;

    public async Task<CheckoutSessionResult> CreateSessionAsync(
        CreateCheckoutSessionRequest request,
        CancellationToken ct = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async (token) =>
        {
            var client = await clientFactory.GetClientAsync(token);
            var service = new SessionService(client);

            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            UnitAmount = (long)(request.Amount * 100),
                            Currency = request.Currency,
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = $"Order {request.OrderId}",
                            },
                        },
                        Quantity = 1,
                    },
                },
                Mode = "payment",
                SuccessUrl = request.SuccessUrl,
                CancelUrl = request.CancelUrl,
                CustomerEmail = request.CustomerEmail,
                Metadata = request.Metadata is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(request.Metadata),
                ClientReferenceId = request.OrderId.ToString(),
            };

            var requestOptions = new RequestOptions { IdempotencyKey = request.IdempotencyKey };
            var session = await service.CreateAsync(options, requestOptions, token);

            return new CheckoutSessionResult(
                session.Id,
                session.Url,
                session.PaymentIntentId,
                PaymentProvider.Stripe);
        }, ct);
    }
}
