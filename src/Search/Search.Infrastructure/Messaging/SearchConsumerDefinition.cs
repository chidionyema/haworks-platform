using MassTransit;

namespace Haworks.Search.Infrastructure.Messaging;

/// <summary>
/// Search consumers update Elasticsearch, not Postgres. No EF outbox needed.
/// This definition exists to provide consistent retry policy via UseMessageRetry.
/// </summary>
public sealed class SearchConsumerDefinition<TConsumer>
    : ConsumerDefinition<TConsumer>
    where TConsumer : class, IConsumer
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<TConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)));
    }
}
