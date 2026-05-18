using MassTransit;

namespace Haworks.BuildingBlocks.Messaging;

/// <summary>
/// Per-consumer retry presets. Use in ConsumerDefinition to override
/// the global ConfigureStandardRabbitMq policy for specific consumers.
///
/// Usage:
///   protected override void ConfigureConsumer(
///       IReceiveEndpointConfigurator endpoint,
///       IConsumerConfigurator&lt;MyConsumer&gt; consumer)
///   {
///       endpoint.UseMessageRetry(RetryPresets.ExternalApi);
///   }
/// </summary>
public static class RetryPresets
{
    /// <summary>
    /// Default: fast fail for internal events (3x, 9s total).
    /// Use for: saga events, cache invalidation, audit writes.
    /// </summary>
    public static void Fast(IRetryConfigurator r) => r.Incremental(3,
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

    /// <summary>
    /// External APIs: longer backoff for transient outages (5x, ~2.5min immediate + delayed redelivery).
    /// Use for: Stripe, PayPal, EasyPost, Nominatim, ClamAV.
    /// </summary>
    public static void ExternalApi(IRetryConfigurator r) => r.Exponential(5,
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5));

    /// <summary>
    /// Saga events: handles race conditions where DB state isn't ready yet (4x, ~30s).
    /// Use for: consumers that depend on saga state written by another consumer.
    /// </summary>
    public static void SagaEvent(IRetryConfigurator r) => r.Incremental(4,
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));

    /// <summary>
    /// Background/batch: tolerates long delays (3x, ~5min).
    /// Use for: audit ingestion, analytics flush, non-latency-sensitive writes.
    /// </summary>
    public static void Background(IRetryConfigurator r) => r.Intervals(
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(5));
}
