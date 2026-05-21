using MassTransit;

namespace Haworks.BffWeb.Api.Infrastructure.Messaging;

/// <summary>
/// BffWeb consumers are SignalR bridges — no DbContext, no outbox.
/// This definition provides retry policy only.
/// </summary>
public sealed class BffWebConsumerDefinition<TConsumer>
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
