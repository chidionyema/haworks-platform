using Microsoft.Extensions.DependencyInjection;

namespace Haworks.Audit.Application;

/// <summary>
/// L1.D fills in this body — registers <c>IAuditExportJob</c>, the
/// <c>AuditExportWorker</c> hosted service, and the
/// <c>PartitionRolloverService</c> hosted service. L0 ships the empty
/// stub so the orchestrator can call it.
/// </summary>
public static class AuditExportRegistration
{
    public static IServiceCollection AddAuditExport(this IServiceCollection services)
    {
        // L1.D: register IAuditExportJob + AuditExportWorker (HostedService)
        // + PartitionRolloverService (HostedService) here.
        return services;
    }
}
