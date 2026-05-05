using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Hybrid;

namespace Haworks.Catalog.Api.Controllers;

/// <summary>
/// Demo-only endpoints used by the portfolio site's interactive demos via
/// BffWeb. NOT part of catalog-svc's domain — these are deliberately
/// minimal HTTP surfaces that BffWeb's typed clients can hit to drive
/// patterns like circuit breakers against a real downstream service.
///
/// Access: <see cref="AllowAnonymousAttribute"/> because the demo surface
/// is public; per-session rate limiting handled at the BffWeb edge.
/// </summary>
[ApiController]
[Route("demo")]
[AllowAnonymous]
public sealed class DemoTestController(
    HybridCache cache,
    ILogger<DemoTestController> logger) : ControllerBase
{
    /// <summary>
    /// Always returns 503 ServiceUnavailable. Used by T2.3's circuit-breaker
    /// demo: BffWeb hits this endpoint via a typed HttpClient with a Polly
    /// circuit breaker; 2 consecutive 503s open the circuit. Subsequent
    /// "shouldFail=false" calls hit /health and reset the circuit.
    /// </summary>
    [HttpGet("fail")]
    public IActionResult AlwaysFail() =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            error = "demo_failure",
            message = "Synthetic failure for circuit-breaker demo",
            timestamp = DateTime.UtcNow,
        });

    /// <summary>
    /// T2.6: real cache-stampede demo. Fires N concurrent reads through
    /// HybridCache.GetOrCreateAsync against a fresh key (clears first to
    /// guarantee a miss). HybridCache's built-in singleflight collapses
    /// the concurrent factory invocations into one — only one DB-simulated
    /// hit happens regardless of N. The dbQueries counter proves it.
    ///
    /// Without singleflight (protectionMode='none'), this endpoint runs
    /// the factory directly per request — N hits.
    /// </summary>
    [HttpPost("cache-stampede")]
    public async Task<IActionResult> Stampede([FromBody] StampedeRequest request, CancellationToken ct)
    {
        var key = $"demo:stampede:{request.CacheKey}:{Guid.NewGuid():N}";
        var dbQueries = 0;

        async ValueTask<string> Factory(CancellationToken token)
        {
            Interlocked.Increment(ref dbQueries);
            await Task.Delay(request.SimulatedDbLatencyMs, token);
            return $"value-{Guid.NewGuid():N}";
        }

        if (request.ProtectionMode == "singleflight")
        {
            // HybridCache collapses concurrent factories for the same key.
            await Parallel.ForEachAsync(
                Enumerable.Range(0, request.ConcurrentRequests),
                ct,
                async (_, token) => await cache.GetOrCreateAsync(key, Factory, cancellationToken: token));
        }
        else
        {
            // Bypass cache — every request hits the factory directly.
            await Parallel.ForEachAsync(
                Enumerable.Range(0, request.ConcurrentRequests),
                ct,
                async (_, token) => await Factory(token));
        }

        logger.LogInformation(
            "Cache stampede demo: mode={Mode} concurrency={N} dbQueries={Q}",
            request.ProtectionMode, request.ConcurrentRequests, dbQueries);

        return Ok(new
        {
            sessionId = Guid.NewGuid(),
            protectionMode = request.ProtectionMode,
            cacheHits = request.ConcurrentRequests - dbQueries,
            cacheMisses = dbQueries,
            dbQueries,
        });
    }

    public sealed record StampedeRequest(int ConcurrentRequests, string CacheKey, string ProtectionMode, int SimulatedDbLatencyMs);
}
