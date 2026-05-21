using Haworks.CheckoutOrchestrator.Domain;
using Haworks.Contracts.Checkout;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Haworks.CheckoutOrchestrator.Infrastructure.Workers;

/// <summary>
/// Belt-and-braces fallback for the saga's stock-reservation timeout.
///
/// The primary timeout is wired via MassTransit's
/// <c>Schedule&lt;StockReservationTimedOutEvent&gt;</c> on the saga (5 min).
/// If the delayed-message-exchange plugin is missing or the broker drops
/// the scheduled message, sagas stuck in Initiated will sit indefinitely
/// with no stock reserved (low financial risk but operational visibility gap).
///
/// This watcher polls every 60s, finds sagas stuck in Initiated past 5 min,
/// and publishes <see cref="StockReservationTimedOutEvent"/> directly.
/// </summary>
public sealed class StockReservationTimeoutWatcher : BackgroundService
{
    private static readonly TimeSpan TimeoutDeadline = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    private const int MaxPublishesPerTick = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StockReservationTimeoutWatcher> _logger;

    public StockReservationTimeoutWatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<StockReservationTimeoutWatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!await DelaySafeAsync(TimeSpan.FromSeconds(20), stoppingToken)) return;

            while (!stoppingToken.IsCancellationRequested)
            {
                await TickSafeAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { /* graceful shutdown */ }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Unhandled exception in {ServiceName}", nameof(StockReservationTimeoutWatcher));
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
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { /* graceful shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StockReservationTimeoutWatcher tick failed; will retry next interval");
        }

        try { await Task.Delay(PollInterval, stoppingToken); }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckoutDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var deadline = DateTime.UtcNow - TimeoutDeadline;

        var stuck = await db.Set<CheckoutSagaState>()
            .Where(s => s.CurrentState == "Initiated" && s.CreatedAt < deadline)
            .OrderBy(s => s.CreatedAt)
            .Take(MaxPublishesPerTick)
            .Select(s => new { s.CorrelationId, s.OrderId })
            .ToListAsync(ct);

        if (stuck.Count == 0) return;

        _logger.LogInformation(
            "StockReservationTimeoutWatcher: publishing timeout for {Count} stuck saga(s)",
            stuck.Count);

        foreach (var saga in stuck)
        {
            try
            {
                await publishEndpoint.Publish(
                    new StockReservationTimedOutEvent
                    {
                        SagaId = saga.CorrelationId,
                        OrderId = saga.OrderId,
                    }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "StockReservationTimeoutWatcher: failed to publish for saga {SagaId}; will retry next tick",
                    saga.CorrelationId);
            }
        }
    }
}
