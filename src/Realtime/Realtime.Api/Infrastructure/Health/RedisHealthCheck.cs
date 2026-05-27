using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Haworks.Realtime.Api.Infrastructure.Health;

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisHealthCheck> _logger;

    public RedisHealthCheck(IConnectionMultiplexer redis, ILogger<RedisHealthCheck> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var pingResult = await db.PingAsync();
            return pingResult.TotalMilliseconds < 1000
                ? HealthCheckResult.Healthy($"Redis responded in {pingResult.TotalMilliseconds}ms")
                : HealthCheckResult.Degraded($"Redis slow response: {pingResult.TotalMilliseconds}ms");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis health check failed");
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }
}
