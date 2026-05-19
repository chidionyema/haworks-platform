using MassTransit;
using Microsoft.Extensions.Logging;

namespace Haworks.BuildingBlocks.Messaging;

/// <summary>
/// Observes receive faults (messages that exhaust retries and move to _error queues).
/// Logs the fault with full context so dead-letter messages are never silent.
/// </summary>
public sealed class DiagnosticReceiveObserver(ILogger<DiagnosticReceiveObserver> logger) : IReceiveObserver
{
    public Task PreReceive(ReceiveContext context) => Task.CompletedTask;

    public Task PostReceive(ReceiveContext context) => Task.CompletedTask;

    public Task PostConsume<T>(ConsumeContext<T> context, TimeSpan duration, string consumerType) where T : class
        => Task.CompletedTask;

    public Task ConsumeFault<T>(ConsumeContext<T> context, TimeSpan duration, string consumerType, Exception exception) where T : class
        => Task.CompletedTask;

    public Task ReceiveFault(ReceiveContext context, Exception exception)
    {
        logger.LogCritical(
            exception,
            "RECEIVE FAULT (dead-letter): Endpoint={Endpoint}, MessageId={MessageId}, ContentType={ContentType}",
            context.InputAddress,
            context.GetMessageId(),
            context.ContentType);
        return Task.CompletedTask;
    }
}
