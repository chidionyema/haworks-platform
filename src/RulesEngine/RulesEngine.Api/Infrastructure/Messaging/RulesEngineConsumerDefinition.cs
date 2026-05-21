using Haworks.BuildingBlocks.Messaging;
using MassTransit;

namespace Haworks.RulesEngine.Api.Infrastructure.Messaging;

public sealed class RulesEngineConsumerDefinition<TConsumer>
    : BoundedContextConsumerDefinition<TConsumer, RulesDbContext>
    where TConsumer : class, IConsumer { }
