using Microsoft.Extensions.DependencyInjection;

namespace Haworks.Audit.Application;

/// <summary>
/// L1.C fills in this body — registers MediatR query handlers for
/// <c>GetAuditEventsQuery</c> + <c>GetAuditEventByIdQuery</c> and the
/// FluentValidation validator. L0 ships the empty stub so the
/// orchestrator can call it.
/// </summary>
public static class AuditQueriesRegistration
{
    public static IServiceCollection AddAuditQueries(this IServiceCollection services)
    {
        // L1.C: register MediatR + GetAuditEventsQueryValidator here.
        return services;
    }
}
