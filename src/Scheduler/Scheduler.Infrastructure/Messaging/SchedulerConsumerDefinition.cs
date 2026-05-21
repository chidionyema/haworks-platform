using Haworks.BuildingBlocks.Messaging;
using Haworks.Scheduler.Infrastructure.Persistence;
using MassTransit;

namespace Haworks.Scheduler.Infrastructure.Messaging;

public sealed class SchedulerConsumerDefinition<TConsumer>
    : BoundedContextConsumerDefinition<TConsumer, SchedulerDbContext>
    where TConsumer : class, IConsumer { }
