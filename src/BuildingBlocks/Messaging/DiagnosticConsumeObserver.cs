using MassTransit;
using Microsoft.Extensions.Logging;

namespace Haworks.BuildingBlocks.Messaging;

/// <summary>
/// Platform-wide consume observer. Logs every consumer fault including
/// sagas. ConsumeFault fires when a message exhausts all retries and
/// is moved to the error queue.
/// </summary>
public sealed class DiagnosticConsumeObserver(ILogger<DiagnosticConsumeObserver> logger) : IConsumeObserver
{
    public Task PreConsume<T>(ConsumeContext<T> context) where T : class
    {
        logger.LogWarning(
            "PRE_CONSUME: {MessageType} on {Endpoint}, MessageId={MessageId}, CorrelationId={CorrelationId}",
            typeof(T).Name,
            context.ReceiveContext.InputAddress,
            context.MessageId,
            context.CorrelationId);
        return Task.CompletedTask;
    }

    public Task PostConsume<T>(ConsumeContext<T> context) where T : class
    {
        logger.LogWarning(
            "POST_CONSUME: {MessageType} on {Endpoint}, MessageId={MessageId}, CorrelationId={CorrelationId}",
            typeof(T).Name,
            context.ReceiveContext.InputAddress,
            context.MessageId,
            context.CorrelationId);
        return Task.CompletedTask;
    }

    public Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception) where T : class
    {
        logger.LogError(
            exception,
            "CONSUMER FAULT (dead-lettered): {MessageType} on {Endpoint}, MessageId={MessageId}, CorrelationId={CorrelationId}",
            typeof(T).Name,
            context.ReceiveContext.InputAddress,
            context.MessageId,
            context.CorrelationId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Fires on EVERY retry attempt — not just the final fault. This is
/// critical for saga debugging because MassTransit retries 3x with
/// incremental backoff + 3x delayed redelivery before dead-lettering.
/// Without this, a DbUpdateConcurrencyException from the Npgsql 9 xmin
/// bug silently retries for 36 minutes before surfacing in ConsumeFault.
/// </summary>
public sealed class DiagnosticRetryObserver(ILogger<DiagnosticRetryObserver> logger) : IRetryObserver
{
    public Task PreRetry<T>(RetryContext<T> context) where T : class, PipeContext
        => Task.CompletedTask;

#pragma warning disable S2325
    public Task PostRetry<T>(RetryContext<T> context) where T : class, PipeContext
        => Task.CompletedTask;
#pragma warning restore S2325

    public Task RetryFault<T>(RetryContext<T> context) where T : class, PipeContext
    {
        var consumeContext = context as ConsumeContext;
        logger.LogWarning(
            context.Exception,
            "RETRY FAULT: attempt {RetryCount}, {ExceptionType}: {Message}, Endpoint={Endpoint}, CorrelationId={CorrelationId}",
            context.RetryCount,
            context.Exception?.GetType().Name,
            context.Exception?.Message,
            consumeContext?.ReceiveContext?.InputAddress,
            consumeContext?.CorrelationId);
        return Task.CompletedTask;
    }

    public Task PostCreate<T>(RetryPolicyContext<T> context) where T : class, PipeContext
        => Task.CompletedTask;

    public Task RetryComplete<T>(RetryContext<T> context) where T : class, PipeContext
        => Task.CompletedTask;

    public Task PostFault<T>(RetryContext<T> context) where T : class, PipeContext
    {
        var consumeContext = context as ConsumeContext;
        logger.LogError(
            context.Exception,
            "RETRY EXHAUSTED: retries exhausted (last attempt {RetryCount}), {ExceptionType}: {Message}, Endpoint={Endpoint}, CorrelationId={CorrelationId}",
            context.RetryCount,
            context.Exception?.GetType().Name,
            context.Exception?.Message,
            consumeContext?.ReceiveContext?.InputAddress,
            consumeContext?.CorrelationId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Observes receive faults — messages that can't even be deserialized
/// or that exhaust all retry + redelivery and hit the error queue.
/// </summary>
public sealed class DiagnosticReceiveObserver(ILogger<DiagnosticReceiveObserver> logger) : IReceiveObserver
{
    public Task PreReceive(ReceiveContext context) => Task.CompletedTask;
    public Task PostReceive(ReceiveContext context) => Task.CompletedTask;
    public Task PostConsume<T>(ConsumeContext<T> context, TimeSpan duration, string consumerType)
        where T : class
    {
        logger.LogWarning(
            "RECV_POST_CONSUME: {MessageType} by {ConsumerType}, Duration={Duration}ms, Endpoint={Endpoint}, CorrelationId={CorrelationId}",
            typeof(T).Name,
            consumerType,
            duration.TotalMilliseconds,
            context.ReceiveContext.InputAddress,
            context.CorrelationId);
        return Task.CompletedTask;
    }

    public Task ConsumeFault<T>(ConsumeContext<T> context, TimeSpan duration, string consumerType, Exception exception)
        where T : class
    {
        logger.LogError(
            exception,
            "RECV_CONSUME_FAULT: {MessageType} by {ConsumerType}, Duration={Duration}ms, Endpoint={Endpoint}, CorrelationId={CorrelationId}",
            typeof(T).Name,
            consumerType,
            duration.TotalMilliseconds,
            context.ReceiveContext.InputAddress,
            context.CorrelationId);
        return Task.CompletedTask;
    }

    public Task ReceiveFault(ReceiveContext context, Exception exception)
    {
        logger.LogCritical(
            exception,
            "RECEIVE FAULT (undeliverable): Endpoint={Endpoint}, ContentType={ContentType}",
            context.InputAddress,
            context.ContentType);
        return Task.CompletedTask;
    }
}
