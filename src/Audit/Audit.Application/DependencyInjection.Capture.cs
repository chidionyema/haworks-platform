using Microsoft.Extensions.DependencyInjection;

namespace Haworks.Audit.Application;

/// <summary>
/// L1.B fills in this body — registers <c>IAuditWriter</c> (singleton,
/// COPY-batched) and <c>IAuditConsumerRegistry</c>. L0 ships the empty
/// stub so the orchestrator can call it.
/// </summary>
public static class AuditCaptureRegistration
{
    public static IServiceCollection AddAuditCapture(this IServiceCollection services)
    {
        // L1.B: register IAuditWriter + IAuditConsumerRegistry here.
        return services;
    }
}
