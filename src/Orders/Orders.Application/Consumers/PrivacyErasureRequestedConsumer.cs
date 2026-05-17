using Haworks.BuildingBlocks.Messaging;
using Haworks.Contracts.Privacy;
using Haworks.Orders.Domain.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Haworks.Orders.Application.Consumers;

/// <summary>
/// Handles GDPR erasure for orders-svc: anonymises all orders belonging
/// to the requesting user then publishes <see cref="PrivacyErasureCompleted"/>
/// so the Privacy saga can track completion across all bounded contexts.
/// </summary>
public sealed class PrivacyErasureRequestedConsumer(
    IOrderRepository orders,
    IDomainEventPublisher eventPublisher,
    ILogger<PrivacyErasureRequestedConsumer> logger
) : IConsumer<PrivacyErasureRequested>
{
    private const int PageSize = 200;

    public async Task Consume(ConsumeContext<PrivacyErasureRequested> context)
    {
        var msg = context.Message;
        var userId = msg.UserId.ToString();

        logger.LogInformation(
            "GDPR erasure requested for UserId={UserId}, RequestId={RequestId}",
            msg.UserId, msg.RequestId);

        var totalAnonymised = 0;
        while (true)
        {
            // Always fetch from offset 0 — previously-anonymised rows won't
            // match the userId filter any more after SaveChanges.
            var batch = await orders.ListByUserTrackedAsync(userId, 0, PageSize, context.CancellationToken);
            if (batch.Count == 0) break;

            foreach (var order in batch)
            {
                order.AnonymiseForPrivacy();
            }

            await orders.SaveChangesAsync(context.CancellationToken);
            totalAnonymised += batch.Count;
        }

        logger.LogInformation("Anonymised {Count} orders for UserId={UserId}", totalAnonymised, msg.UserId);

        // Use IDomainEventPublisher (outbox-backed) instead of context.Publish
        // so the completion event commits in the same EF transaction as the
        // final batch of anonymised orders.
        await eventPublisher.PublishAsync(new PrivacyErasureCompleted
        {
            RequestId = msg.RequestId,
            UserId = msg.UserId,
            ServiceName = "orders-svc"
        });
        await orders.SaveChangesAsync(context.CancellationToken);
    }
}
