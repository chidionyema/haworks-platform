using Haworks.Contracts.Privacy;
using Haworks.Privacy.Application.Requests.Sagas;
using Haworks.Privacy.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Haworks.Privacy.Infrastructure.Workers;

/// <summary>
/// Belt-and-braces fallback for the saga's 7-day GDPR erasure timeout.
///
/// The primary timeout is wired via MassTransit's
/// <c>Schedule&lt;PrivacyErasureTimedOut&gt;</c> on the saga, which delegates
/// to the broker's delayed-message mechanism. If that mechanism fails or the
/// broker silently drops the scheduled message, sagas stuck in Processing
/// past 7 days will sit indefinitely with erasure incomplete.
///
/// This watcher polls every hour, finds any stuck saga past the 7-day
/// deadline, and re-publishes <see cref="PrivacyErasureRequested"/> to
/// re-trigger the downstream services that haven't reported back. The saga
/// stays in Processing so the normal ErasureCompleted handlers still work.
///
/// Late duplicates (scheduler + watcher both fire) are harmless: if the
/// saga already transitioned to Stalled, the DuringAny guards in the state
/// machine silently no-op the re-published events.
/// </summary>
public sealed class ErasureStalledWatcher : BackgroundService
{
    private static readonly TimeSpan StalledDeadline = TimeSpan.FromDays(7);
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);
    private const int MaxPublishesPerTick = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ErasureStalledWatcher> _logger;

    public ErasureStalledWatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<ErasureStalledWatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!await DelaySafeAsync(TimeSpan.FromSeconds(30), stoppingToken)) return;

            while (!stoppingToken.IsCancellationRequested)
            {
                await TickSafeAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Unhandled exception in background service {ServiceName}", nameof(ErasureStalledWatcher));
            throw;
        }
    }

    private static async Task<bool> DelaySafeAsync(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); return true; }
        catch (OperationCanceledException) { return false; }
    }

    private async Task TickSafeAsync(CancellationToken stoppingToken)
    {
        try
        {
            await TickAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ErasureStalledWatcher tick failed; will retry next interval");
        }

        try { await Task.Delay(PollInterval, stoppingToken); }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PrivacyDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var deadline = DateTime.UtcNow - StalledDeadline;

        // C2: include Stalled sagas — the watcher re-drives any service not yet completed.
        // Select the full completion flags so we can publish targeted re-requests.
        var stuck = await db.Set<PrivacyRequestState>()
            .Where(s => (s.CurrentState == "Processing" || s.CurrentState == "Stalled")
                        && s.CreatedAt < deadline)
            .OrderBy(s => s.CreatedAt)
            .Take(MaxPublishesPerTick)
            .Select(s => new
            {
                s.CorrelationId,
                s.UserId,
                s.IdentityCompleted,
                s.OrdersCompleted,
                s.PaymentsCompleted
            })
            .ToListAsync(ct);

        if (stuck.Count == 0) return;

        _logger.LogWarning(
            "ErasureStalledWatcher: re-publishing PrivacyErasureRequested for {Count} stalled saga(s)",
            stuck.Count);

        foreach (var saga in stuck)
        {
            // C2: only re-publish for services that haven't reported completion yet.
            // A service that already completed must not be re-triggered.
            var pendingServices = new List<string>(3);
            if (!saga.IdentityCompleted) pendingServices.Add("identity-svc");
            if (!saga.OrdersCompleted)   pendingServices.Add("orders-svc");
            if (!saga.PaymentsCompleted) pendingServices.Add("payments-svc");

            if (pendingServices.Count == 0)
            {
                // All services completed — watcher is racing the saga; skip.
                continue;
            }

            _logger.LogWarning(
                "ErasureStalledWatcher: stalled erasure request {RequestId} for user {UserId} — pending: {Pending}",
                saga.CorrelationId, saga.UserId, string.Join(", ", pendingServices));

            try
            {
                // Deterministic MessageId based on CorrelationId so downstream
                // consumers see the same MessageId on every re-publish tick, enabling
                // MassTransit inbox deduplication instead of creating unique messages.
                await publishEndpoint.Publish(
                    new PrivacyErasureRequested { RequestId = saga.CorrelationId, UserId = saga.UserId },
                    ctx => { ctx.MessageId = saga.CorrelationId; },
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "ErasureStalledWatcher: failed to publish for saga {SagaId}; will retry next tick",
                    saga.CorrelationId);
            }
        }
    }
}
