using MassTransit;

namespace Haworks.BuildingBlocks.Messaging;

/// <summary>
/// MassTransit send-pipeline filter that throws <see cref="RelayPausedException"/>
/// while <see cref="RelayPauseGate.IsPaused"/> is true. Throwing in the
/// outbox dispatcher's send path keeps the <c>OutboxMessage</c> row
/// marked as "not yet delivered"; the next <c>BusOutboxDeliveryService</c>
/// tick (default 1s) re-attempts and either fails again (still paused) or
/// succeeds and removes the row.
///
/// Wire it into the bus via:
///   cfg.UseSendFilter(typeof(RelayPauseFilter&lt;&gt;), context);
/// </summary>
public sealed class RelayPauseFilter<T> : IFilter<SendContext<T>>
    where T : class
{
    public Task Send(SendContext<T> context, IPipe<SendContext<T>> next)
    {
        if (RelayPauseGate.IsPaused)
        {
            throw new RelayPausedException(
                $"Relay paused: send of {typeof(T).Name} blocked. Outbox row will retry on next tick.");
        }
        return next.Send(context);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("relay-pause-gate");
}

/// <summary>
/// Thrown by <see cref="RelayPauseFilter{T}"/> when the relay is paused.
/// Caught by MassTransit's outbox dispatcher; row stays undelivered.
/// </summary>
public sealed class RelayPausedException : Exception
{
    public RelayPausedException(string message) : base(message) { }
}
