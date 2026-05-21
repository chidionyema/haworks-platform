namespace Haworks.Webhooks.Application.Interfaces;

/// <summary>
/// Dispatches webhook events to partner endpoints via Svix.
/// Svix handles retry, signing, delivery tracking, and SSRF protection.
/// </summary>
public interface IWebhookDispatcher
{
    /// <summary>
    /// Forwards a platform event to all Svix endpoints registered under the given partner application.
    /// </summary>
    /// <param name="partnerId">The partner (Svix application) identifier.</param>
    /// <param name="eventType">The event type, e.g. "order.created".</param>
    /// <param name="payload">JSON-serialized event payload.</param>
    /// <param name="eventId">Idempotency key for the event (Svix deduplicates on this).</param>
    /// <param name="ct">Cancellation token.</param>
    Task ForwardAsync(Guid partnerId, string eventType, string payload, string eventId, CancellationToken ct);
}
