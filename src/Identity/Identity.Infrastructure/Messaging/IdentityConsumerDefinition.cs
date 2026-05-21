using Haworks.BuildingBlocks.Messaging;
using MassTransit;

namespace Haworks.Identity.Infrastructure.Messaging;

public sealed class IdentityConsumerDefinition<TConsumer>
    : BoundedContextConsumerDefinition<TConsumer, AppIdentityDbContext>
    where TConsumer : class, IConsumer { }
