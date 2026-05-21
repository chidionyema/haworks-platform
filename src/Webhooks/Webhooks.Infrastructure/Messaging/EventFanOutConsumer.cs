using Haworks.Contracts.Orders;
using Haworks.Contracts.Payments;
using Haworks.Webhooks.Application.Interfaces;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Haworks.Webhooks.Infrastructure.Messaging;

/// <summary>
/// MassTransit consumer that receives platform domain events and forwards them
/// to partner webhook endpoints via Svix. Resolves active subscriptions from
/// the local DB, then delegates all dispatch/retry/signing to Svix.
/// </summary>
public sealed class EventFanOutConsumer(
    IWebhooksDbContext db,
    IWebhookDispatcher dispatcher,
    ILogger<EventFanOutConsumer> logger) :
    IConsumer<OrderCreatedEvent>,
    IConsumer<OrderCompletedEvent>,
    IConsumer<OrderAbandonedEvent>,
    IConsumer<PaymentCompletedEvent>,
    IConsumer<RefundIssuedEvent>
{
    public Task Consume(ConsumeContext<OrderCreatedEvent> context) => FanOutAsync(context, "order.created", context.Message);
    public Task Consume(ConsumeContext<OrderCompletedEvent> context) => FanOutAsync(context, "order.completed", context.Message);
    public Task Consume(ConsumeContext<OrderAbandonedEvent> context) => FanOutAsync(context, "order.abandoned", context.Message);
    public Task Consume(ConsumeContext<PaymentCompletedEvent> context) => FanOutAsync(context, "payment.completed", context.Message);
    public Task Consume(ConsumeContext<RefundIssuedEvent> context) => FanOutAsync(context, "refund.issued", context.Message);

    private async Task FanOutAsync<T>(ConsumeContext<T> context, string externalEventName, T data) where T : class
    {
        var eventId = context.MessageId?.ToString() ?? Guid.NewGuid().ToString();

        // Resolve active subscriptions that listen for this event type
        var subscriptions = await db.Subscriptions
            .AsNoTracking()
            .Where(s => s.IsActive && s.DeletedAt == null && s.Events.Contains(externalEventName))
            .ToListAsync(context.CancellationToken);

        if (subscriptions.Count == 0) return;

        logger.LogInformation(
            "Fanning out event {EventName} to {Count} partners via Svix",
            externalEventName, subscriptions.Count);

        // Prepare the canonical event payload
        var payload = JsonSerializer.Serialize(new
        {
            @event = externalEventName,
            id = eventId,
            deliveredAt = DateTime.UtcNow,
            data
        });

        // Forward to Svix for each partner — Svix handles retry, signing, SSRF, delivery tracking.
        // Group by PartnerId to avoid duplicate Svix app-creation calls within the same fan-out.
        var partnerIds = subscriptions.Select(s => s.PartnerId).Distinct();
        foreach (var partnerId in partnerIds)
        {
            await dispatcher.ForwardAsync(partnerId, externalEventName, payload, eventId, context.CancellationToken);
        }
    }
}
