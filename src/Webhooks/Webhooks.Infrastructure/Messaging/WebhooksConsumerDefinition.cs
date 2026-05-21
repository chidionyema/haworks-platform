using Haworks.BuildingBlocks.Messaging;
using Haworks.Webhooks.Infrastructure.Persistence;
using MassTransit;

namespace Haworks.Webhooks.Infrastructure.Messaging;

public sealed class WebhooksConsumerDefinition<TConsumer>
    : BoundedContextConsumerDefinition<TConsumer, WebhooksDbContext>
    where TConsumer : class, IConsumer { }
