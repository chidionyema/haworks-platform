using MassTransit;
using Microsoft.Extensions.Logging;

namespace Haworks.BuildingBlocks.Messaging;

/// <summary>
/// Platform-wide observer that logs every consumer fault to stdout.
/// MassTransit's inbox/outbox can swallow exceptions silently — this
/// ensures they always surface in structured logs (and therefore in
/// flyctl logs, CloudWatch, Seq, etc.).
/// </summary>
public sealed class DiagnosticConsumeObserver(ILogger<DiagnosticConsumeObserver> logger) : IConsumeObserver
{
    public Task PreConsume<T>(ConsumeContext<T> context) where T : class
    {
        logger.LogDebug(
            "Consuming {MessageType} on {ConsumerType} — MessageId={MessageId}",
            typeof(T).Name,
            context.ReceiveContext.InputAddress,
            context.MessageId);
        return Task.CompletedTask;
    }

    public Task PostConsume<T>(ConsumeContext<T> context) where T : class
    {
        logger.LogDebug(
            "Consumed {MessageType} — MessageId={MessageId}, Duration={Duration}ms",
            typeof(T).Name,
            context.MessageId,
            context.ReceiveContext.ElapsedTime.TotalMilliseconds);
        return Task.CompletedTask;
    }

    public Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception) where T : class
    {
        logger.LogError(
            exception,
            "CONSUMER FAULT: {MessageType} on {Endpoint} — MessageId={MessageId}, CorrelationId={CorrelationId}",
            typeof(T).Name,
            context.ReceiveContext.InputAddress,
            context.MessageId,
            context.CorrelationId);
        return Task.CompletedTask;
    }
}
