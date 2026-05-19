using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Haworks.BuildingBlocks.Authentication;
using Haworks.BffWeb.Infrastructure.Authentication;

namespace Haworks.BffWeb.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment env)
    {
        // MassTransit + the PaymentSessionCreatedConsumer are wired by the Api
        // project's Program.cs (it owns the consumer type, which lives under
        // BffWeb.Api/SignalR/). Calling AddMassTransit in two places throws
        // ConfigurationException — see ADR-0010 footnote in CHANGELOG.
        if (env.IsEnvironment("Test"))
        {
            return services;
        }

        // Service-to-service JWT: BFF obtains a token from Identity for internal calls
        // Requires: ServiceAuth__SharedSecret + Services__Identity__BaseUrl on Fly
        services.AddHttpClient("IdentityServiceToken");
        services.AddSingleton<IServiceTokenProvider, BffServiceTokenProvider>();

        return services;
    }
}
