using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Haworks.Shipping.Api.Infrastructure;

public sealed class EasyPostHealthCheck : IHealthCheck
{
    private readonly IShippingProvider _provider;
    private readonly ILogger<EasyPostHealthCheck> _logger;

    public EasyPostHealthCheck(IShippingProvider provider, ILogger<EasyPostHealthCheck> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Test EasyPost connectivity by creating a simple tracking request
            // This is a lightweight operation that validates API connectivity
            await _provider.GetTrackingAsync("test-tracking-code", cancellationToken);
            return HealthCheckResult.Healthy("EasyPost API is accessible");
        }
        catch (ArgumentException)
        {
            // ArgumentException for invalid tracking code is expected and indicates the API is working
            return HealthCheckResult.Healthy("EasyPost API is accessible (test tracking code rejected as expected)");
        }
        catch (InvalidOperationException ex) when (ex.InnerException is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "EasyPost API connectivity issue");
            return HealthCheckResult.Unhealthy("Cannot connect to EasyPost API", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EasyPost health check failed");
            return HealthCheckResult.Unhealthy("EasyPost API health check failed", ex);
        }
    }
}