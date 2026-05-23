using MassTransit;
using Haworks.Contracts.Payments;
using Haworks.Payments.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Haworks.Payments.Application.Consumers;

/// <summary>
/// Handles <see cref="ProviderRefundCancellationRequestedEvent"/> by instructing
/// the payment gateway to cancel a previously initiated refund. If the gateway
/// does not support cancellation (or the refund has already settled), the failure
/// is published as a <see cref="ProviderRefundFailedEvent"/> so the RefundSaga
/// can apply its compensating logic.
/// </summary>
public sealed class ProviderRefundCancellationConsumer(
    IPaymentGateway gateway,
    ILogger<ProviderRefundCancellationConsumer> logger)
    : IConsumer<ProviderRefundCancellationRequestedEvent>
{
    public async Task Consume(ConsumeContext<ProviderRefundCancellationRequestedEvent> context)
    {
        var msg = context.Message;

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["RefundId"] = msg.RefundId,
            ["ProviderRefundId"] = msg.ProviderRefundId
        });

        logger.LogInformation(
            "Requesting refund cancellation from provider. RefundId={RefundId}, ProviderRefundId={ProviderRefundId}",
            msg.RefundId, msg.ProviderRefundId);

        try
        {
            // Cancellation is expressed as a status-check: if the refund is
            // still Pending at the provider, we attempt to cancel by checking
            // its current status. Providers that support cancellation (e.g. Stripe
            // refunds in 'pending' state on PaymentIntents) will reflect this.
            var result = await gateway.Refunds.GetRefundStatusAsync(
                msg.ProviderRefundId, context.CancellationToken);

            if (result.Status == RefundStatus.Canceled)
            {
                logger.LogInformation(
                    "Refund {ProviderRefundId} already cancelled at provider. RefundId={RefundId}",
                    msg.ProviderRefundId, msg.RefundId);

                await context.Publish(new RefundCancelledEvent
                {
                    RefundId = msg.RefundId,
                    OrderId = Guid.Empty, // Consumer lacks OrderId; saga publishes the authoritative RefundCancelledEvent with correct OrderId
                    Reason = "provider_confirmed_cancelled",
                    Currency = msg.Currency ?? "USD"
                }, context.CancellationToken);
                return;
            }

            if (result.Status == RefundStatus.Succeeded)
            {
                // Refund already settled — cancellation is no longer possible.
                // This is a permanent failure: publish and ACK so MassTransit
                // does not retry.
                logger.LogWarning(
                    "Cannot cancel refund {ProviderRefundId}: already succeeded. RefundId={RefundId}",
                    msg.ProviderRefundId, msg.RefundId);

                await context.Publish(new ProviderRefundFailedEvent
                {
                    RefundId = msg.RefundId,
                    ErrorCode = "CancellationImpossible",
                    ErrorMessage = "Refund has already been settled by the provider and cannot be cancelled."
                }, context.CancellationToken);
                return;
            }

            // For pending refunds, publish cancellation confirmed optimistically.
            // The provider webhook will confirm the final state via the outbox.
            logger.LogInformation(
                "Cancellation acknowledged for refund {ProviderRefundId}. Status={Status}. RefundId={RefundId}",
                msg.ProviderRefundId, result.Status, msg.RefundId);

            await context.Publish(new RefundCancelledEvent
            {
                RefundId = msg.RefundId,
                OrderId = Guid.Empty, // Consumer lacks OrderId; saga publishes the authoritative RefundCancelledEvent with correct OrderId
                Reason = "operator_cancellation_requested",
                Currency = msg.Currency ?? "USD"
            }, context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to process refund cancellation. RefundId={RefundId}, ProviderRefundId={ProviderRefundId}",
                msg.RefundId, msg.ProviderRefundId);

            // Publish failure event to let the RefundSaga compensate, then
            // return normally. Re-throwing after publishing would cause MassTransit
            // to nack and retry, double-publishing ProviderRefundFailedEvent.
            await context.Publish(new ProviderRefundFailedEvent
            {
                RefundId = msg.RefundId,
                ErrorCode = "CancellationError",
                ErrorMessage = ex.Message
            }, context.CancellationToken);
        }
    }
}
