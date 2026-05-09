using Microsoft.Extensions.DependencyInjection;

namespace Haworks.Audit.Application;

/// <summary>
/// L1.A fills in this body — registers <c>IAuditExtractor&lt;T&gt;</c>
/// implementations for every <c>Haworks.Contracts.IDomainEvent</c>, plus
/// <c>ISecretRedactor</c>. L0 ships the empty stub so the orchestrator
/// can call it.
/// </summary>
public static class AuditExtractorsRegistration
{
    public static IServiceCollection AddAuditExtractors(this IServiceCollection services)
    {
        // L1.A: register ReflectionAuditExtractor<T> + per-event overrides
        // (StockReservationFailedEvent, VaultRotationStageEvent,
        // ProductCacheInvalidatedEvent) + ISecretRedactor here.
        return services;
    }
}
