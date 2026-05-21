using Haworks.Webhooks.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Svix;
using Svix.Models;

namespace Haworks.Webhooks.Infrastructure.Svix;

/// <summary>
/// Thin adapter that forwards platform events to Svix.
/// Svix handles retry with exponential backoff, HMAC signing,
/// delivery tracking, SSRF protection, and rate limiting.
/// </summary>
public sealed class SvixWebhookForwarder(
    SvixClient svix,
    ILogger<SvixWebhookForwarder> logger) : IWebhookDispatcher
{
    public async Task ForwardAsync(
        Guid partnerId,
        string eventType,
        string payload,
        string eventId,
        CancellationToken ct)
    {
        var appId = partnerId.ToString();

        // GetOrCreateAsync is idempotent: creates if new, returns existing if uid matches.
        await svix.Application.GetOrCreateAsync(
            new ApplicationIn { Name = $"partner-{appId}", Uid = appId },
            cancellationToken: ct);

        // Create the message — Svix deduplicates on EventId within a 5-minute window.
        await svix.Message.CreateAsync(
            appId,
            new MessageIn
            {
                EventType = eventType,
                Payload = payload,
                EventId = eventId
            },
            cancellationToken: ct);

        logger.LogInformation(
            "Forwarded event {EventType} (id={EventId}) to Svix app {AppId}",
            eventType, eventId, appId);
    }
}
