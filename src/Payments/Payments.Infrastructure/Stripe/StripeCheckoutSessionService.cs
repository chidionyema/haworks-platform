using Haworks.Payments.Application.Interfaces;
using Haworks.Contracts.Payments;
using Haworks.Payments.Infrastructure.Options;
using Haworks.BuildingBlocks.Caching;
using Haworks.BuildingBlocks.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Stripe;
using Stripe.Checkout;

namespace Haworks.Payments.Infrastructure.Stripe;

internal sealed class StripeCheckoutSessionService : ICheckoutSessionService
{
    private readonly IStripeClientFactory _clientFactory;
    private readonly IPaymentSessionCache _cache;
    // Combined policy: retry (3x w/ jitter), circuit breaker (open at 5
    // failures for 30s), bulkhead (limits concurrency to keep a stuck
    // Stripe call from exhausting the saga consumer's thread pool).
    // ResilienceOptions.Stripe is the platform's pre-configured profile.
    private readonly IAsyncPolicy _policy;

    public StripeCheckoutSessionService(
        IStripeClientFactory clientFactory,
        IPaymentSessionCache cache,
        IResiliencePolicyFactory policyFactory)
    {
        _clientFactory = clientFactory;
        _cache = cache;
        _policy = policyFactory.CreateCombinedPolicy(ResilienceOptions.Stripe);
    }

    public Task<CheckoutSessionResult> CreateSessionAsync(
        CreateCheckoutSessionRequest request,
        CancellationToken ct = default)
        => _policy.ExecuteAsync(async (_, token) =>
        {
            var client = await _clientFactory.GetClientAsync(token);
            var service = new SessionService(client);

            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { StripeConstants.PaymentMethods.Card },
                LineItems = request.LineItems.Select(item => new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = item.UnitAmountCents,
                        Currency = item.Currency ?? "USD",
                        ProductData = new SessionLineItemPriceDataProductDataOptions { Name = item.Name }
                    },
                    Quantity = item.Quantity
                }).ToList(),
                Mode = StripeConstants.SessionModes.Payment,
                SuccessUrl = request.SuccessUrl,
                CancelUrl = request.CancelUrl,
                Metadata = request.Metadata != null ? new Dictionary<string, string>(request.Metadata) : new Dictionary<string, string>()
            };

            var requestOptions = new RequestOptions { IdempotencyKey = request.IdempotencyKey };
            var session = await service.CreateAsync(options, requestOptions, token);

            await _cache.SetAsync(session.Id, Guid.Parse(options.Metadata["orderId"]), options.Metadata["userId"], token);

            return new CheckoutSessionResult
            {
                SessionId = session.Id,
                SessionUrl = session.Url,
                Provider = PaymentProvider.Stripe
            };
        }, new Context(), ct);

    public Task<CheckoutSession?> GetSessionAsync(string sessionId, CancellationToken ct = default)
        => _policy.ExecuteAsync<CheckoutSession?>(async (_, token) =>
        {
            var client = await _clientFactory.GetClientAsync(token);
            var service = new SessionService(client);
            var session = await service.GetAsync(sessionId, cancellationToken: token);

            return new CheckoutSession
            {
                SessionId = session.Id,
                Status = session.Status == "complete" ? SessionStatus.Complete : SessionStatus.Open,
                TransactionId = session.PaymentIntentId,
                AmountTotal = session.AmountTotal ?? 0,
                Currency = session.Currency,
                Provider = PaymentProvider.Stripe
            };
        }, new Context(), ct);

    public Task<bool> ExpireSessionAsync(string sessionId, CancellationToken ct = default)
        => _policy.ExecuteAsync(async (_, token) =>
        {
            var client = await _clientFactory.GetClientAsync(token);
            var service = new SessionService(client);
            await service.ExpireAsync(sessionId, cancellationToken: token);
            return true;
        }, new Context(), ct);
}
