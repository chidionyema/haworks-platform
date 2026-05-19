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
        var skip = 0;
        while (true)
        {
            // Paginate through results without saving, using skip to advance
            var batch = await orders.ListByUserTrackedAsync(userId, skip, PageSize, context.CancellationToken);
            if (batch.Count == 0) break;

            foreach (var order in batch)
            {
                order.AnonymiseForPrivacy();
            }

            skip += batch.Count;
            totalAnonymised += batch.Count;
        }

        logger.LogInformation("Anonymised {Count} orders for UserId={UserId}", totalAnonymised, msg.UserId);

        await context.Publish(new PrivacyErasureCompleted
        {
            RequestId = msg.RequestId,
            UserId = msg.UserId,
            ServiceName = "orders-svc"
        });
    }
}
