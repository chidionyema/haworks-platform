using Haworks.Webhooks.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;
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

        var retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    logger.LogWarning(
                        "Svix API call failed (attempt {RetryCount}/3). Retrying in {Delay}ms. Error: {Error}",
                        retryCount, timespan.TotalMilliseconds, outcome.Exception?.Message);
                });

        try
        {
            await retryPolicy.ExecuteAsync(async () =>
            {
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
            });

            logger.LogInformation(
                "Forwarded event {EventType} (id={EventId}) to Svix app {AppId}",
                eventType, eventId, appId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to forward event {EventType} (id={EventId}) to Svix after 3 attempts. Event may be lost.",
                eventType, eventId);
            throw;
        }
    }
}
