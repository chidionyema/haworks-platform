using System.Diagnostics.Metrics;
using MassTransit;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Haworks.BuildingBlocks.Messaging;

/// <summary>
/// Catches ALL faulted messages across the bus, emits structured error logs,
/// increments fault metrics with exception classification, and detects
/// poison messages (same MessageId failing repeatedly after replays).
/// </summary>
public sealed class GlobalFaultConsumer : IConsumer<Fault>
{
    private static readonly Meter Meter = new("Haworks.MassTransit", "1.0.0");
    private static readonly Counter<long> FaultCounter = Meter.CreateCounter<long>(
        "masstransit.faults.total",
        description: "Total faulted messages by consumer and exception type");
    private static readonly Counter<long> PoisonCounter = Meter.CreateCounter<long>(
        "masstransit.poison_messages.total",
        description: "Messages that failed repeatedly after replays — require manual intervention");

    private const int PoisonThreshold = 3;

    private static readonly HashSet<string> TransientExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.TimeoutException",
        "System.Net.Http.HttpRequestException",
        "Npgsql.NpgsqlException",
        "RabbitMQ.Client.Exceptions.AlreadyClosedException",
        "System.IO.IOException",
        "System.OperationCanceledException",
    };

    private readonly ILogger<GlobalFaultConsumer> _logger;
    private readonly IDistributedCache? _cache;

    public GlobalFaultConsumer(ILogger<GlobalFaultConsumer> logger, IDistributedCache? cache = null)
    {
        _logger = logger;
        _cache = cache;
    }

    public async Task Consume(ConsumeContext<Fault> context)
    {
        var fault = context.Message;
        var exceptions = fault.Exceptions ?? [];
        var consumerType = context.SourceAddress?.Segments.LastOrDefault() ?? "Unknown";
        var messageId = context.MessageId?.ToString() ?? fault.FaultId.ToString();

        // Poison message detection: track how many times this MessageId has faulted
        await DetectPoisonAsync(messageId, consumerType);

        foreach (var ex in exceptions)
        {
            var isTransient = TransientExceptions.Contains(ex.ExceptionType);

            FaultCounter.Add(1,
                new KeyValuePair<string, object?>("consumer", consumerType),
                new KeyValuePair<string, object?>("exception_type", ex.ExceptionType),
                new KeyValuePair<string, object?>("is_transient", isTransient));

            _logger.LogError(
                "DLQ: Faulted message (FaultId={FaultId}, MessageId={MessageId}, Consumer={Consumer}, " +
                "Transient={IsTransient}). Exception: {ExceptionType}: {ExceptionMessage}",
                fault.FaultId,
                messageId,
                consumerType,
                isTransient,
                ex.ExceptionType,
                ex.Message);
        }

        if (exceptions.Length == 0)
        {
            FaultCounter.Add(1,
                new KeyValuePair<string, object?>("consumer", consumerType),
                new KeyValuePair<string, object?>("exception_type", "None"),
                new KeyValuePair<string, object?>("is_transient", false));

            _logger.LogError(
                "DLQ: Faulted message (FaultId={FaultId}, MessageId={MessageId}, Consumer={Consumer}) — no exception details",
                fault.FaultId,
                messageId,
                consumerType);
        }
    }

    private async Task DetectPoisonAsync(string messageId, string consumerType)
    {
        if (_cache is null) return;

        var key = $"dlq:fail:{messageId}";
        try
        {
            var raw = await _cache.GetStringAsync(key);
            var count = raw is null ? 0 : int.Parse(raw);
            count++;

            await _cache.SetStringAsync(key, count.ToString(),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7) });

            if (count >= PoisonThreshold)
            {
                PoisonCounter.Add(1,
                    new KeyValuePair<string, object?>("consumer", consumerType));

                _logger.LogCritical(
                    "POISON MESSAGE: {MessageId} has failed {Count} times (consumer={Consumer}). " +
                    "Do NOT replay — inspect payload in RabbitMQ Management UI and fix root cause.",
                    messageId, count, consumerType);
            }
        }
        catch (Exception ex)
        {
            // Cache failure must not break fault handling
            _logger.LogDebug(ex, "Poison detection cache operation failed for {MessageId}", messageId);
        }
    }
}
