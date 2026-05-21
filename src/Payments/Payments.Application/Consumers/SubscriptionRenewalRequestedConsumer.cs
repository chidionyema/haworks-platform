using MassTransit;
using Haworks.Contracts.Payments;
using Haworks.Payments.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Haworks.Payments.Application.Consumers;

public sealed class SubscriptionRenewalRequestedConsumer(
    IPaymentGateway paymentGateway,
    ILogger<SubscriptionRenewalRequestedConsumer> logger)
    : IConsumer<SubscriptionRenewalRequestedEvent>
{
    public async Task Consume(ConsumeContext<SubscriptionRenewalRequestedEvent> context)
    {
        var msg = context.Message;
        logger.LogInformation("Processing renewal request for subscription {SubscriptionId}", msg.ProviderSubscriptionId);

        try
        {
            // H12: ISubscriptionManager.HandleSubscriptionEventAsync returns void — the renewal
            // amount, currency, userId, and exact period-end are not available here.
            // The authoritative SubscriptionRenewedEvent (with real financial data) is published
            // by StripeSubscriptionManager / PayPalSubscriptionManager via their webhook paths
            // when the provider confirms the renewal. This consumer's publish is a belt-and-
            // suspenders fallback for the saga scheduler path; downstream handlers MUST NOT rely
            // on AmountCents/Currency from this event for financial records.
            await paymentGateway.Subscriptions.HandleSubscriptionEventAsync(new SubscriptionEvent
            {
                SubscriptionId = msg.ProviderSubscriptionId,
                EventType = SubscriptionEventType.Renewed,
                NewStatus = SubscriptionStatus.Active,
                Provider = paymentGateway.ActiveProvider
            }, context.CancellationToken);

            logger.LogInformation("Subscription {SubscriptionId} renewed successfully", msg.ProviderSubscriptionId);

            logger.LogWarning(
                "SubscriptionRenewedEvent for {SubscriptionId} published with placeholder financial data " +
                "(AmountCents=0, Currency=unknown). Authoritative data comes from the provider webhook path.",
                msg.ProviderSubscriptionId);

            await context.Publish(new SubscriptionRenewedEvent
            {
                ProviderSubscriptionId = msg.ProviderSubscriptionId,
                UserId = string.Empty,
                Provider = paymentGateway.ActiveProvider,
                AmountCents = 0,         // not available without provider response; see H12 comment above
                Currency = string.Empty, // not available without provider response; see H12 comment above
                NewPeriodEnd = DateTime.UtcNow.AddMonths(1), // approximation; webhook path carries exact value
                RenewedAt = DateTime.UtcNow
            }, context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Subscription renewal failed for {SubscriptionId}", msg.ProviderSubscriptionId);

            // Publish failure to saga and RETURN — do not rethrow.
            // Rethrowing after publishing causes MassTransit to retry,
            // which can publish both Failed and Renewed events (race).
            await context.Publish(new SubscriptionRenewalFailedEvent
            {
                SubscriptionId = msg.SubscriptionId,
                ErrorCode = ex.GetType().Name,
                ErrorMessage = ex.Message
            }, context.CancellationToken);
        }
    }
}
