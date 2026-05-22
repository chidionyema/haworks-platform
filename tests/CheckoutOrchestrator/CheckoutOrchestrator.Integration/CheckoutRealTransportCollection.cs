using Xunit;

namespace Haworks.CheckoutOrchestrator.Integration;

/// <summary>
/// Shared collection for tests that use real RabbitMQ transport.
/// Both SagaRealTransportTests and InterceptorWiringTests connect to
/// the same SharedTestRabbitMq queue — running them in parallel causes
/// consumer message stealing. This collection ensures they share one
/// factory and run sequentially.
/// </summary>
[CollectionDefinition(Name)]
public sealed class CheckoutRealTransportCollection : ICollectionFixture<CheckoutRealTransportFactory>
{
    public const string Name = "CheckoutRealTransport";
}
