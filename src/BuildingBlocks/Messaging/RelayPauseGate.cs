namespace Haworks.BuildingBlocks.Messaging;

/// <summary>
/// Process-wide flag that gates the MassTransit publish pipeline. When
/// <see cref="IsPaused"/> is true, the <c>RelayPauseFilter&lt;T&gt;</c> on
/// the bus's send pipeline throws — failed sends keep their
/// <c>OutboxMessage</c> rows undelivered, so messages back up in the
/// per-context EF outbox until the flag is released. On
/// <see cref="Resume"/> the next <c>BusOutboxDeliveryService</c> tick
/// (default 1s) flushes the backlog to RabbitMQ.
///
/// Used by the portfolio site's events-flow demo so a viewer can see real
/// outbox-anchored messages get queued, paused, and released — with the
/// 'persisted' / 'consumed' SignalR stages firing in real time as the
/// drain happens.
///
/// Single-process scope: each service has its own copy of this flag.
/// Catalog can be paused while orders is not. For the demo only payments
/// matters (it's the publisher behind the events/trigger endpoint).
/// </summary>
public static class RelayPauseGate
{
    private static volatile bool s_isPaused;

    public static bool IsPaused => s_isPaused;

    /// <summary>Set the gate; subsequent publishes will throw until <see cref="Resume"/>.</summary>
    public static void Pause() => s_isPaused = true;

    /// <summary>Clear the gate; the outbox dispatcher will drain on its next tick.</summary>
    public static void Resume() => s_isPaused = false;
}
