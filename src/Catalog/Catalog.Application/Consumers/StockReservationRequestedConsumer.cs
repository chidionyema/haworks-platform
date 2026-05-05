using MassTransit;
using Haworks.Contracts.Catalog;
using Haworks.Contracts.Checkout;

namespace Haworks.Catalog.Application.Consumers;

/// <summary>
/// Reservation consumer. Triggered by checkout-svc' CheckoutSaga when a new
/// checkout is initiated. Reserves each requested item via
/// <see cref="Product.ReserveStock"/> and publishes either
/// <see cref="StockReservedEvent"/> (all items reserved) or
/// <see cref="StockReservationFailedEvent"/> (any item insufficient / unknown).
///
/// Closes the Phase 4 gap: the saga publishes
/// <see cref="StockReservationRequestedEvent"/>; before this consumer existed
/// nothing in the platform handled it and the saga stalled at Initiated.
/// The synchronous REST flow exposed by <c>ReserveStockCommand</c> stays in
/// place for callers (e.g. tests) that don't want event-driven semantics.
///
/// Idempotency: same layers as the release consumer — MT inbox dedupes
/// transport replays, EF xmin shadow concurrency catches concurrent writers.
///
/// Per ADR-0009 the consumer touches no foreign-context state: only catalog
/// Product aggregates. All cross-context fields needed downstream
/// (TotalAmount, CustomerEmail, OrderLineItems, …) are propagated forward
/// onto the published event so PaymentSession can act without querying out.
/// </summary>
public sealed class StockReservationRequestedConsumer(
    IProductRepository products,
    IDomainEventPublisher eventPublisher,
    ILogger<StockReservationRequestedConsumer> logger
) : IConsumer<StockReservationRequestedEvent>
{
    public async Task Consume(ConsumeContext<StockReservationRequestedEvent> context)
    {
        var evt = context.Message;
        logger.LogInformation(
            "Reserving stock for orderId={OrderId}, sagaId={SagaId}, items={ItemCount}",
            evt.OrderId, evt.SagaId, evt.Items.Count);

        var reserved = new List<StockReservationItem>(evt.Items.Count);
        var failed = new List<FailedReservationItem>();

        foreach (var item in evt.Items)
        {
            var product = await products.GetByIdTrackedAsync(item.ProductId, context.CancellationToken);
            if (product is null)
            {
                failed.Add(new FailedReservationItem
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    RequestedQuantity = item.Quantity,
                    AvailableQuantity = 0,
                });
                continue;
            }

            if (!product.ReserveStock(item.Quantity))
            {
                failed.Add(new FailedReservationItem
                {
                    ProductId = item.ProductId,
                    ProductName = product.Name,
                    RequestedQuantity = item.Quantity,
                    AvailableQuantity = product.StockQuantity,
                });
                continue;
            }

            reserved.Add(new StockReservationItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = item.Quantity,
                RemainingStock = product.StockQuantity,
            });
        }

        if (failed.Count > 0)
        {
            // Don't SaveChanges — EF tracker discards the partial mutations
            // when the consume scope ends, so no stock is silently held.
            logger.LogWarning(
                "Stock reservation failed for orderId={OrderId}; {FailedCount}/{TotalCount} items unavailable",
                evt.OrderId, failed.Count, evt.Items.Count);

            await eventPublisher.PublishAsync(new StockReservationFailedEvent
            {
                OrderId = evt.OrderId,
                SagaId = evt.SagaId,
                FailedItems = failed,
                Reason = $"{failed.Count} of {evt.Items.Count} items unavailable",
            }, context.CancellationToken);
            return;
        }

        // Publish BEFORE save — outbox-friendly. The OutboxMessage row commits
        // in the same EF transaction as the stock decrements; on rollback the
        // publish is rolled back too.
        await eventPublisher.PublishAsync(new StockReservedEvent
        {
            OrderId = evt.OrderId,
            SagaId = evt.SagaId,
            UserId = evt.UserId,
            TotalAmount = evt.TotalAmount,
            Currency = evt.Currency,
            CustomerEmail = evt.CustomerEmail,
            IdempotencyKey = evt.IdempotencyKey,
            Items = reserved,
            OrderLineItems = evt.Items,
        }, context.CancellationToken);

        await products.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation(
            "Reserved stock for orderId={OrderId}; published StockReservedEvent ({Total} units)",
            evt.OrderId, reserved.Sum(i => i.Quantity));
    }
}
