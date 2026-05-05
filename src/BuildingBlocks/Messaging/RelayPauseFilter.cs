using MassTransit;

namespace Haworks.BuildingBlocks.Messaging;

/// <summary>
/// MassTransit publish-pipeline filter that throws <see cref="RelayPausedException"/>
/// while <see cref="RelayPauseGate.IsPaused"/> is true.
///
/// Behavior: when the gate is paused, every <c>IPublishEndpoint.Publish</c>
/// call (including those routed through the EF outbox via
/// <c>UseEntityFrameworkOutbox + UseBusOutbox</c>) fails fast with
/// <see cref="RelayPausedException"/> — no <c>OutboxMessage</c> row is
/// staged, no broker delivery occurs. Callers see a 500 from the publish
/// path and can choose to retry or surface the failure.
///
/// This is publish-source backpressure, NOT outbox-drain pausing. The
/// distinction matters for the portfolio events-flow demo: paused →
/// triggers fail at the source (visible to the user as
/// <c>PaymentsRejected</c>); resumed → triggers succeed normally and the
/// usual outbox → broker → consumer flow runs. There is no "queued
/// while paused" state to drain — that would require gating
/// <c>BusOutboxDeliveryService</c> dispatch instead, which MT does not
/// expose a clean hook for.
///
/// Wire via:
///   cfg.UsePublishFilter(typeof(RelayPauseFilter&lt;&gt;), context);
/// (NOT UseSendFilter — that's only for raw IBus.Send / SendEndpoint
/// pipelines and would silently no-op for publishes.)
/// </summary>
public sealed class RelayPauseFilter<T> : IFilter<PublishContext<T>>
    where T : class
{
    public Task Send(PublishContext<T> context, IPipe<PublishContext<T>> next)
    {
        if (RelayPauseGate.IsPaused)
        {
            throw new RelayPausedException(
                $"Relay paused: publish of {typeof(T).Name} blocked.");
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
