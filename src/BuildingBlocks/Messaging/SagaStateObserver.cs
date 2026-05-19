using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Haworks.BuildingBlocks.Messaging;

/// <summary>
/// EF Core SaveChanges interceptor that logs every saga state INSERT/UPDATE.
/// Fires after SaveChangesAsync completes — reports exactly what was persisted
/// (or not persisted) to the database. Catches the silent-insert-failure class
/// of bugs where the consumer returns success but no row is written.
/// </summary>
public sealed class SagaPersistenceInterceptor : SaveChangesInterceptor
{
    private readonly ILogger<SagaPersistenceInterceptor> _logger;

    public SagaPersistenceInterceptor(ILogger<SagaPersistenceInterceptor> logger)
    {
        _logger = logger;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null) return ValueTask.FromResult(result);

        foreach (var entry in eventData.Context.ChangeTracker.Entries())
        {
            // Only log saga-related entities (tables ending in "Sagas" or "SagaState")
            var tableName = entry.Metadata.GetTableName();
            if (tableName is null || (!tableName.Contains("Saga") && !tableName.Contains("Inbox") && !tableName.Contains("Outbox")))
                continue;

            var correlationId = entry.Property("CorrelationId")?.CurrentValue
                             ?? entry.Property("MessageId")?.CurrentValue;

            if (entry.State == EntityState.Added)
            {
                _logger.LogInformation(
                    "SAGA_DB INSERT: {Table} CorrelationId={CorrelationId}, RowsAffected={Rows}",
                    tableName, correlationId, result);
            }
            else if (entry.State == EntityState.Modified)
            {
                var currentState = entry.Property("CurrentState")?.CurrentValue;
                _logger.LogInformation(
                    "SAGA_DB UPDATE: {Table} CorrelationId={CorrelationId}, State={State}, RowsAffected={Rows}",
                    tableName, correlationId, currentState, result);
            }
        }

        if (result == 0)
        {
            _logger.LogWarning(
                "SAGA_DB ZERO_ROWS: SaveChangesAsync returned 0 — no rows written. Schema={Schema}",
                eventData.Context.GetType().Name);
        }

        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null) return ValueTask.FromResult(result);

        foreach (var entry in eventData.Context.ChangeTracker.Entries())
        {
            var tableName = entry.Metadata.GetTableName();
            if (tableName is null || !tableName.Contains("Saga")) continue;

            if (entry.State == EntityState.Added)
            {
                var correlationId = entry.Property("CorrelationId")?.CurrentValue;
                _logger.LogInformation(
                    "SAGA_DB PRE-INSERT: {Table} CorrelationId={CorrelationId}, State={EntityState}",
                    tableName, correlationId, entry.State);
            }
        }

        return ValueTask.FromResult(result);
    }
}
