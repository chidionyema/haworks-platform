using Haworks.BuildingBlocks.Messaging;
using Haworks.Audit.Infrastructure.Persistence;
using MassTransit;

namespace Haworks.Audit.Infrastructure.Messaging;

public sealed class AuditConsumerDefinition<TConsumer>
    : BoundedContextConsumerDefinition<TConsumer, AuditDbContext>
    where TConsumer : class, IConsumer { }
