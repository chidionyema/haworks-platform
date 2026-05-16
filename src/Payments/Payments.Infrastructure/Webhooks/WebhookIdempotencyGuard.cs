using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Haworks.Payments.Application.Interfaces;
using Haworks.Payments.Domain;
using Haworks.Payments.Domain.Interfaces;
using Haworks.Contracts.Payments;

namespace Haworks.Payments.Infrastructure.Webhooks;

/// <summary>
/// Infrastructure guard for webhook idempotency.
/// Uses a two-layer check: Distributed Cache (Redis) and the DB.
/// </summary>
internal sealed class WebhookIdempotencyGuard : IWebhookIdempotencyGuard
{
    private readonly IPaymentRepository _repository;
    private readonly IDistributedCache _cache;

    private const string CacheKeyPrefix = "webhook:";

    public WebhookIdempotencyGuard(
        IPaymentRepository repository,
        IDistributedCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<bool> IsAlreadyProcessedAsync(
        PaymentProvider provider,
        string providerEventId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(providerEventId)) return false;

        var cacheKey = $"{CacheKeyPrefix}{provider}:{providerEventId}";
        var cached = await _cache.GetAsync(cacheKey, ct);
        if (cached != null) return true;

        var exists = await _repository.WebhookEventExistsAsync(provider, providerEventId, ct);
        
        if (exists)
        {
            // Backfill cache to avoid DB hits on duplicate retries
            await _cache.SetAsync(cacheKey, new byte[] { 1 }, 
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) }, ct);
        }

        return exists;
    }

    public async Task MarkProcessedAsync(
        PaymentProvider provider,
        string providerEventId,
        string eventType,
        CancellationToken ct = default)
    {
        // EventJson is mapped to jsonb (NOT NULL); empty string fails Postgres'
        // JSON parser. The dedup row only needs to exist — payload contents are
        // not the source of truth — so an empty JSON object is sufficient.
        var webhookEvent = WebhookEvent.Create(provider, providerEventId, eventType, "{}");
        await _repository.AddWebhookEventAsync(webhookEvent, ct);
        await _repository.SaveChangesAsync(ct);

        var cacheKey = $"{CacheKeyPrefix}{provider}:{providerEventId}";
        await _cache.SetAsync(cacheKey, new byte[] { 1 }, 
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) }, ct);
    }
}
