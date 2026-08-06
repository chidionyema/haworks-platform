using Haworks.Payments.Application.Interfaces;
using Haworks.Contracts.Payments;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;

namespace Haworks.Payments.Infrastructure.Stripe;

internal sealed class StripeSubscriptionService(
    IStripeClientFactory clientFactory,
    ILogger<StripeSubscriptionService> logger) : ISubscriptionService
{

    public async Task<CheckoutSessionResult> CreateSubscriptionSessionAsync(
        CreateSubscriptionSessionRequest request,
        CancellationToken ct = default)
    {
        logger.LogInformation("Creating subscription checkout session for plan {PlanId}, user {UserId}",
            request.PlanId, request.UserId);

        var client = await clientFactory.GetClientAsync(ct);
        var service = new SessionService(client);

        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = new List<string> { StripeConstants.PaymentMethods.Card },
            LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions { Price = request.PlanId, Quantity = 1 }
            },
            Mode = StripeConstants.SessionModes.Subscription,
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            CustomerEmail = request.CustomerEmail,
            Metadata = request.Metadata != null ? new Dictionary<string, string>(request.Metadata) : new Dictionary<string, string>()
        };

        if (!options.Metadata.ContainsKey("user_id")) options.Metadata["user_id"] = request.UserId;

        var requestOptions = new RequestOptions { IdempotencyKey = request.IdempotencyKey };
        var session = await service.CreateAsync(options, requestOptions, ct);

        logger.LogInformation("Subscription session {SessionId} created for plan {PlanId}",
            session.Id, request.PlanId);

        return new CheckoutSessionResult
        {
            SessionId = session.Id,
            SessionUrl = session.Url,
            Provider = PaymentProvider.Stripe
        };
    }
}
