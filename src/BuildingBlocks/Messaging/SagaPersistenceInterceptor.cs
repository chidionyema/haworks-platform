using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Haworks.BuildingBlocks.Messaging;

/// <summary>
/// Platform-wide EF Core SaveChanges interceptor for saga observability.
/// Covers all four operational requirements:
///
/// 1. State transitions: logs "Saga X transitioned from Initial to Initiated"
/// 2. Persistence proof: logs INSERT/UPDATE with row count
/// 3. Inbox/saga split detection: warns when inbox commits but saga doesn't
///
/// Audit trail writes are handled by SagaTransitionAuditObserver (IStateMachineObserver),
/// NOT by this interceptor. Interceptors must not mutate the change tracker during SaveChanges.
/// Registered via AddPlatformInterceptors(sp) on all 14 DbContexts.
/// </summary>
public sealed class SagaPersistenceInterceptor : SaveChangesInterceptor
{
    private readonly ILogger<SagaPersistenceInterceptor> _logger;

    public SagaPersistenceInterceptor(ILogger<SagaPersistenceInterceptor> logger)
    {
        _logger = logger;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null) return ValueTask.FromResult(result);

        var context = eventData.Context;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (!IsSagaEntity(entry)) continue;

            var correlationId = GetCorrelationId(entry);

            if (entry.State == EntityState.Added)
            {
                var currentState = entry.CurrentValues["CurrentState"]?.ToString() ?? "unknown";
                _logger.LogInformation(
                    "SAGA INSERT: {SagaType} CorrelationId={CorrelationId}, InitialState={State}, Table={Table}",
                    entry.Metadata.ClrType.Name,
                    correlationId,
                    currentState,
                    entry.Metadata.GetTableName());
            }
            else if (entry.State == EntityState.Modified)
            {
                var previousState = entry.OriginalValues["CurrentState"]?.ToString() ?? "unknown";
                var currentState = entry.CurrentValues["CurrentState"]?.ToString() ?? "unknown";

                _logger.LogInformation(
                    "SAGA TRANSITION: {SagaType} CorrelationId={CorrelationId}, {PreviousState} -> {CurrentState}, Table={Table}",
                    entry.Metadata.ClrType.Name,
                    correlationId,
                    previousState,
                    currentState,
                    entry.Metadata.GetTableName());
            }
        }

        // Detect inbox-only saves (inbox entry without saga entry = split transaction)
        var hasInbox = false;
        var hasSaga = false;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            var tableName = entry.Metadata.GetTableName() ?? "";
            if (tableName.Contains("InboxState")) hasInbox = true;
            if (IsSagaEntity(entry)) hasSaga = true;
        }
        if (hasInbox && !hasSaga)
        {
            _logger.LogWarning(
                "SAGA SPLIT: InboxState is being saved WITHOUT a saga state entity in the same transaction. " +
                "This indicates the inbox and saga repository are not sharing the same DbContext transaction. " +
                "Context={ContextType}",
                context.GetType().Name);
        }

        return ValueTask.FromResult(result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null) return ValueTask.FromResult(result);

        foreach (var entry in eventData.Context.ChangeTracker.Entries())
        {
            if (!IsSagaEntity(entry)) continue;

            var correlationId = GetCorrelationId(entry);

            if (result == 0)
            {
                _logger.LogError(
                    "SAGA ZERO_ROWS: SaveChanges returned 0 for {SagaType} CorrelationId={CorrelationId}. " +
                    "The saga state was NOT persisted. Check for xmin conflicts, RLS policies, or transaction rollback.",
                    entry.Metadata.ClrType.Name,
                    correlationId);
            }
            else
            {
                _logger.LogInformation(
                    "SAGA PERSISTED: {SagaType} CorrelationId={CorrelationId}, RowsAffected={Rows}",
                    entry.Metadata.ClrType.Name,
                    correlationId,
                    result);
            }
        }

        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is null) return;

        foreach (var entry in eventData.Context.ChangeTracker.Entries())
        {
            if (!IsSagaEntity(entry)) continue;

            var correlationId = GetCorrelationId(entry);
            _logger.LogError(
                eventData.Exception,
                "SAGA SAVE_FAILED: {SagaType} CorrelationId={CorrelationId}, Exception={ExceptionType}: {Message}",
                entry.Metadata.ClrType.Name,
                correlationId,
                eventData.Exception.GetType().Name,
                eventData.Exception.Message);
        }
    }

    private static bool IsSagaEntity(EntityEntry entry)
    {
        if (typeof(ISaga).IsAssignableFrom(entry.Metadata.ClrType))
            return true;
        if (typeof(SagaStateMachineInstance).IsAssignableFrom(entry.Metadata.ClrType))
            return true;
        var tableName = entry.Metadata.GetTableName() ?? "";
        return tableName.Contains("Saga", StringComparison.OrdinalIgnoreCase);
    }

    private static Guid GetCorrelationId(EntityEntry entry)
    {
        try
        {
            return (Guid)(entry.Property("CorrelationId").CurrentValue ?? Guid.Empty);
        }
        catch
        {
            return Guid.Empty;
        }
    }
}
