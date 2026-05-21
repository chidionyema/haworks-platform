using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Haworks.BuildingBlocks.Messaging;

/// <summary>
/// Per-bounded-context consumer definitions that anchor each consumer's
/// receive endpoint to the OUTBOX of its OWN bounded-context DbContext.
///
/// The MT consume-side outbox filter writes the inbox row + any consumer-issued
/// publishes into TDbContext's tables, all in a single TDbContext transaction.
/// This means a consumer using TDbContext for business state has business + inbox
/// + outbox in ONE TDbContext transaction — no cross-context coordination needed.
///
/// In a microservice topology this is the only sensible wiring shape: a service
/// has only its own DbContext registered and would have nothing else to anchor to.
/// Per-context wiring carries cleanly across the service boundary.
///
/// Subclass this in each service's MT registration to declare the service's
/// canonical consumer-definition type, e.g.:
/// <code>
/// public sealed class CatalogConsumerDefinition&lt;TConsumer&gt;
///     : BoundedContextConsumerDefinition&lt;TConsumer, CatalogDbContext&gt;
///     where TConsumer : class, IConsumer { }
/// </code>
/// </summary>
public abstract class BoundedContextConsumerDefinition<TConsumer, TDbContext>
    : ConsumerDefinition<TConsumer>
    where TConsumer : class, IConsumer
    where TDbContext : DbContext
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<TConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Consume-side filter: opens a TDbContext transaction around Consume,
        // writes InboxState row for dedupe, captures every publish into
        // TDbContext's OutboxMessage table, calls SaveChangesAsync + commits
        // atomically when Consume returns.
        endpointConfigurator.UseEntityFrameworkOutbox<TDbContext>(context);
    }
}

/// <summary>
/// Saga state-machine equivalent of <see cref="BoundedContextConsumerDefinition{TConsumer, TDbContext}"/>.
/// Wires <c>UseEntityFrameworkOutbox&lt;TDbContext&gt;</c> on the saga's
/// receive endpoint so saga inbox + saga state writes + saga publishes all
/// commit atomically in ONE TDbContext transaction.
/// </summary>
#pragma warning disable S2326
public abstract class BoundedContextSagaDefinition<TSaga, TDbContext>
#pragma warning restore S2326
    : SagaDefinition<TSaga>
    where TSaga : class, ISaga
    where TDbContext : DbContext
{
    protected override void ConfigureSaga(
        IReceiveEndpointConfigurator endpointConfigurator,
        ISagaConfigurator<TSaga> sagaConfigurator,
        IRegistrationContext context)
    {
        // Delayed redelivery MUST be outermost — it catches after immediate
        // retries are exhausted and re-queues with increasing delay (1/5/30 min).
        // Previously this was declared AFTER UseMessageRetry, making it dead code.
        endpointConfigurator.UseDelayedRedelivery(r => r.Intervals(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(30)));

        // Immediate retries sit inside redelivery scope. Each retry gets a
        // fresh DI scope + clean DbContext.
        endpointConfigurator.UseMessageRetry(r =>
        {
            r.Interval(5, TimeSpan.FromMilliseconds(500));
            r.Handle<Microsoft.EntityFrameworkCore.DbUpdateException>();
            r.Handle<Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException>();
            r.Handle<Npgsql.NpgsqlException>();
            r.Handle<TimeoutException>();
            r.Handle<System.IO.IOException>();
            var retryObs = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<DiagnosticRetryObserver>(context);
            if (retryObs != null) r.ConnectRetryObserver(retryObs);
        });

        // InMemoryOutbox buffers saga publishes until the saga state is saved.
        // If the save fails, buffered publishes are discarded (not sent).
        // This prevents fire-and-forget publishes to RabbitMQ during compensation.
        endpointConfigurator.UseInMemoryOutbox(context);
    }
}
