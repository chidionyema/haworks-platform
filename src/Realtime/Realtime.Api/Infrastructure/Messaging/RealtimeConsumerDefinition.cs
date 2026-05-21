using MassTransit;

namespace Haworks.Realtime.Api.Infrastructure.Messaging;

/// <summary>
/// Realtime consumers are SignalR relays — no DbContext, no outbox.
/// This definition provides retry policy only.
/// </summary>
public sealed class RealtimeConsumerDefinition<TConsumer>
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
