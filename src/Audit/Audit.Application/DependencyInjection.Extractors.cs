using Haworks.Audit.Application.Extraction;
using Microsoft.Extensions.DependencyInjection;

namespace Haworks.Audit.Application;

public static class AuditExtractorsRegistration
{
    public static IServiceCollection AddAuditExtractors(this IServiceCollection services)
    {
        services.AddExtractors();
        
        // ISecretRedactor will be added in the next step
        
        return services;
    }
}
