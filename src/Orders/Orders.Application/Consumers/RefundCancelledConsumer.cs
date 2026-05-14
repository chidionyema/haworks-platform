using MassTransit;
using Haworks.Contracts.Payments;
using Haworks.Orders.Application.Interfaces;
using Haworks.Orders.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Haworks.Orders.Application.Consumers;

public sealed class RefundCancelledConsumer(
    IOrderRepository orderRepository,
    ILogger<RefundCancelledConsumer> logger) : IConsumer<RefundCancelledEvent>
{
    public async Task Consume(ConsumeContext<RefundCancelledEvent> context)
    {
        var msg = context.Message;
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["OrderId"] = msg.OrderId,
            ["RefundId"] = msg.RefundId
        });

        var order = await orderRepository.GetByIdAsync(msg.OrderId, context.CancellationToken);
        if (order == null)
        {
            logger.LogWarning("Order {OrderId} not found for cancelled refund {RefundId}", msg.OrderId, msg.RefundId);
            return;
        }

        // If a refund is cancelled, it means the payment is still valid (or failed in a way that doesn't refund)
        // We'll flip it back to Paid if it was in a transient state, but usually it's already Paid.
        if (order.Status == OrderStatus.Refunded) // Should not happen if saga is authoritative
        {
            order.UpdateStatus(OrderStatus.Paid);
            await orderRepository.SaveChangesAsync(context.CancellationToken);
            logger.LogInformation("Order {OrderId} status reverted to Paid after refund cancellation", msg.OrderId);
        }
    }
}
