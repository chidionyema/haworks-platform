using Haworks.BuildingBlocks.Messaging;
using Haworks.Pricing.Infrastructure.Persistence;
using MassTransit;

namespace Haworks.Pricing.Infrastructure.Messaging;

public sealed class PricingConsumerDefinition<TConsumer>
    : BoundedContextConsumerDefinition<TConsumer, PricingDbContext>
    where TConsumer : class, IConsumer { }
