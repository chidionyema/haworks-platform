using MassTransit;
using Microsoft.Extensions.Logging;
using Haworks.Contracts.Catalog;
using Haworks.Contracts.Payments;
using Haworks.Orders.Domain;
using Haworks.Orders.Domain.Interfaces;

namespace Haworks.Orders.Application.Consumers;

/// <summary>
/// Consumes <see cref="CheckoutSessionExpiredEvent"/> from payments-svc (via Stripe webhook)
/// and marks the matching Order as <see cref="OrderStatus.Expired"/>, then publishes
/// <see cref="StockReleaseRequestedEvent"/> to return reserved inventory to catalog.
/// </summary>
public sealed class CheckoutSessionExpiredConsumer(
    IOrderRepository orders,
    IPublishEndpoint eventPublisher,
    ILogger<CheckoutSessionExpiredConsumer> logger
) : IConsumer<CheckoutSessionExpiredEvent>
{
    public async Task Consume(ConsumeContext<CheckoutSessionExpiredEvent> context)
    {
        var evt = context.Message;

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["OrderId"] = evt.OrderId,
            ["PaymentId"] = evt.PaymentId,
            ["SessionId"] = evt.SessionId
        });

        logger.LogInformation("Processing CheckoutSessionExpiredEvent for order {OrderId}", evt.OrderId);

        var order = await orders.GetByIdTrackedAsync(evt.OrderId, context.CancellationToken);
        if (order is null)
        {
            logger.LogWarning("Order {OrderId} not found for expired session {SessionId}", evt.OrderId, evt.SessionId);
            return;
        }

        if (order.Status is OrderStatus.Expired or OrderStatus.Paid or OrderStatus.Abandoned)
        {
            logger.LogInformation("Order {OrderId} already in terminal status {Status}, skipping", order.Id, order.Status);
            return;
        }

        // Use tracked entity + domain method so the order update and outbox
        // message commit in a single EF transaction (MassTransit outbox).
        var wasMarked = order.MarkExpired("checkout_session_expired");

        if (!wasMarked)
        {
            logger.LogInformation("Order {OrderId} already processed or in terminal state, skipping", order.Id);
            return;
        }

        // Publish stock release requested event for catalog-svc.
        // Outbox writes the message row in the same EF transaction.
        await eventPublisher.Publish(new StockReleaseRequestedEvent
        {
            OrderId = order.Id,
            SagaId = order.SagaId,
            Reason = "checkout_session_expired",
            Items = order.Items.Select(i => new StockReservationItem
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                Quantity = i.Quantity,
                RemainingStock = null
            }).ToList()
        }, context.CancellationToken);

        logger.LogInformation("Order {OrderId} marked Expired; published StockReleaseRequestedEvent", order.Id);
    }
}
