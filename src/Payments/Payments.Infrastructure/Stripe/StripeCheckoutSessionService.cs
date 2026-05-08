using Haworks.Payments.Application.Interfaces;
using Haworks.Contracts.Payments;
using Haworks.Payments.Infrastructure.Options;
using Haworks.BuildingBlocks.Caching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Haworks.Payments.Infrastructure.Stripe;

internal sealed class StripeCheckoutSessionService(
    IStripeClientFactory clientFactory,
    IPaymentSessionCache cache) : ICheckoutSessionService
{
    public async Task<CheckoutSessionResult> CreateSessionAsync(
        CreateCheckoutSessionRequest request,
        CancellationToken ct = default)
    {
        var client = await clientFactory.GetClientAsync(ct);
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
        var session = await service.CreateAsync(options, requestOptions, ct);

        await cache.SetAsync(session.Id, Guid.Parse(options.Metadata["orderId"]), options.Metadata["userId"], ct);

        return new CheckoutSessionResult
        {
            SessionId = session.Id,
            SessionUrl = session.Url,
            Provider = PaymentProvider.Stripe
        };
    }

    public async Task<CheckoutSession?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var client = await clientFactory.GetClientAsync(ct);
        var service = new SessionService(client);
        var session = await service.GetAsync(sessionId, cancellationToken: ct);

        return new CheckoutSession
        {
            SessionId = session.Id,
            Status = session.Status == "complete" ? SessionStatus.Complete : SessionStatus.Open,
            TransactionId = session.PaymentIntentId,
            AmountTotal = session.AmountTotal ?? 0,
            Currency = session.Currency,
            Provider = PaymentProvider.Stripe
        };
    }

    public async Task<bool> ExpireSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var client = await clientFactory.GetClientAsync(ct);
        var service = new SessionService(client);
        await service.ExpireAsync(sessionId, cancellationToken: ct);
        return true;
    }
}
