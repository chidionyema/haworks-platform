using System;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Haworks.BuildingBlocks.Messaging;

public abstract class IdempotentConsumerBase<TEvent, TDbContext>(
    TDbContext context,
    ILogger logger) : IConsumer<TEvent>
    where TEvent : class
    where TDbContext : DbContext
{
    protected TDbContext DbContext { get; } = context;

    public async Task Consume(ConsumeContext<TEvent> consumeContext)
    {
        var message = consumeContext.Message;
        var idempotencyKey = ResolveIdempotencyKey(message);

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            logger.LogCritical("Consumer dropped message: Idempotency key resolved to null for event {EventName}", typeof(TEvent).Name);
            return;
        }

        // Do NOT call BeginTransactionAsync — MassTransit EF Outbox manages the
        // ambient transaction. Manual transactions conflict with the outbox and
        // can cause nested transactions or outbox messages to commit separately.
        //
        // Pessimistic row lock (FOR UPDATE) via AcquireIdempotencyLockAsync ensures
        // only one consumer processes a given key at a time within the outbox
        // transaction. This avoids catching DbUpdateException for unique violations
        // which poisons the DbContext and breaks the outbox (Architectural Law #3).
        var acquired = await AcquireIdempotencyLockAsync(idempotencyKey, consumeContext.CancellationToken);
        if (!acquired)
        {
            logger.LogInformation("Idempotent skip for key {Key}", idempotencyKey);
            return;
        }

        await ExecuteBusinessLogicAsync(consumeContext, consumeContext.CancellationToken);
        await RecordProcessedAsync(idempotencyKey, consumeContext.CancellationToken);

        // MassTransit EF Outbox calls SaveChangesAsync and commits the
        // ambient transaction when Consume returns successfully.
    }

    protected abstract string ResolveIdempotencyKey(TEvent message);

    /// <summary>
    /// Attempts to acquire a pessimistic row lock on the idempotency key using
    /// a raw SQL <c>SELECT ... FOR UPDATE SKIP LOCKED</c> (or equivalent).
    /// Returns <c>true</c> if the lock was acquired (message not yet processed),
    /// <c>false</c> if the key is already claimed (duplicate — safe to skip).
    /// Implementations MUST NOT call SaveChangesAsync — the outbox owns the commit.
    /// </summary>
    protected abstract Task<bool> AcquireIdempotencyLockAsync(string key, CancellationToken ct);

    protected abstract Task ExecuteBusinessLogicAsync(ConsumeContext<TEvent> context, CancellationToken ct);
    protected abstract Task RecordProcessedAsync(string key, CancellationToken ct);
}
