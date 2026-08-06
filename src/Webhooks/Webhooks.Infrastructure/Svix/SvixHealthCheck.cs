using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Haworks.Webhooks.Infrastructure.Svix;

/// <summary>
/// Verifies the configured Svix server is reachable via its health endpoint.
/// </summary>
public sealed class SvixHealthCheck(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var serverUrl = configuration["Svix:ServerUrl"];
        if (string.IsNullOrEmpty(serverUrl))
        {
            return HealthCheckResult.Unhealthy("Svix:ServerUrl is not configured.");
        }

        try
        {
            var client = httpClientFactory.CreateClient("SvixHealth");
            var response = await client.GetAsync(new Uri(new Uri(serverUrl), "/api/v1/health/"), cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Svix returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Svix unreachable.", ex);
        }
    }
}
