using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Haworks.Contracts.Payments;
using Haworks.Payments.Application.Telemetry;

namespace Haworks.Payments.Application.Consumers;

/// <summary>
/// Consumes <see cref="PaymentWebhookValidatedEvent"/> published by the
/// webhook controller after signature verification. Dispatches by provider
/// + event type to either complete, fail, or flag the matching Payment
/// aggregate, then publishes the appropriate downstream domain event
/// (PaymentCompleted / PaymentSessionFailed / PaymentAmountMismatch /
/// PaymentVerified) via the per-context outbox.
///
/// Idempotency: the controller sets <c>MessageId == sha256(provider:eventId)[..16]</c>,
/// so MassTransit's inbox dedupes redeliveries without us needing to write
/// any extra dedupe logic — the second arrival is silently absorbed.
///
/// Per ADR-0009 this consumer touches no foreign-context state. It does
/// NOT update Order status — orders-svc consumes
/// <c>PaymentCompletedEvent</c> in Phase 4 to do that itself.
/// </summary>
// public (not internal) — MassTransit's AddConsumer<T, TDef>() takes T as
// a generic type argument from Infrastructure DI, which lives in a sibling
// assembly. Internal would be invisible there.
public sealed class PaymentWebhookValidatedConsumer(
    IEnumerable<IWebhookProcessor> processors,
    ILogger<PaymentWebhookValidatedConsumer> logger
) : IConsumer<PaymentWebhookValidatedEvent>
{
    public async Task Consume(ConsumeContext<PaymentWebhookValidatedEvent> context)
    {
        var evt = context.Message;

        using var activity = PaymentsActivities.Source.StartActivity("payments.webhook.handle");
        activity?.SetTag("payment.provider", evt.Provider);
        activity?.SetTag("payment.event_type", evt.EventType);
        activity?.SetTag("payment.provider_event_id", evt.ProviderEventId);

        logger.LogInformation(
            "Processing webhook: provider={Provider}, eventType={EventType}, providerEventId={ProviderEventId}",
            evt.Provider, evt.EventType, evt.ProviderEventId);

        var providerEnum = ParseProvider(evt.Provider);
        if (providerEnum == null)
        {
            logger.LogError("Unknown webhook provider: {Provider}", evt.Provider);
            return;
        }

        var processor = processors.FirstOrDefault(p => p.Provider == providerEnum.Value);
        if (processor == null)
        {
            logger.LogWarning("No processor registered for provider: {Provider}", evt.Provider);
            return;
        }

        // 1. Re-validate and parse the payload
        var validationResult = await processor.ValidateAndParseAsync(
            evt.RawPayload, evt.Signature, context.CancellationToken);

        if (!validationResult.IsValid || validationResult.Event == null)
        {
            logger.LogWarning("Webhook validation failed for {Provider} {EventId}: {Message}",
                evt.Provider, evt.ProviderEventId, validationResult.ErrorMessage);
            return;
        }

        // 2. Process the event. Do NOT catch DbUpdateException for unique violations —
        // it poisons the DbContext and breaks the outbox (Law #3). On a unique constraint
        // violation, the exception propagates to MassTransit which retries; the retry
        // hits the processor's WebhookEventExistsAsync check and skips.
        var result = await processor.ProcessEventAsync(validationResult.Event, context, context.CancellationToken);

        if (result.Processed)
        {
            logger.LogInformation("Webhook {EventId} processed successfully: {Message}",
                evt.ProviderEventId, result.Message);
        }
        else
        {
            logger.LogInformation("Webhook {EventId} skipped or failed: {Message}",
                evt.ProviderEventId, result.Message);
        }
    }

    private static PaymentProvider? ParseProvider(string provider) => provider switch
    {
        "Stripe" => PaymentProvider.Stripe,
        "PayPal" => PaymentProvider.PayPal,
        _ => null,
    };
}
