using Haworks.Payments.Application.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Haworks.Payments.Infrastructure.Health;

internal sealed class PaymentProviderHealthCheck(
    IPaymentGateway gateway) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await gateway.CheckHealthAsync(cancellationToken);
            return status.IsHealthy 
                ? HealthCheckResult.Healthy($"{gateway.ActiveProvider} is responsive")
                : HealthCheckResult.Degraded($"{gateway.ActiveProvider}: {status.Message}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"{gateway.ActiveProvider} unreachable", ex);
        }
    }
}
