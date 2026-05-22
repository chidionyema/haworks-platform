using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Haworks.BuildingBlocks.Messaging;

/// <summary>
/// Generic state machine observer that writes append-only audit trail rows.
/// Each saga wires this via <c>ConnectStateObserver(auditObserver)</c>.
///
/// Because <see cref="IStateObserver{TSaga}"/> is generic, each saga needs
/// its own typed wrapper. Use <see cref="SagaTransitionAuditObserver{TSaga}"/>
/// which is registered as open-generic in DI.
/// </summary>
public sealed class SagaTransitionAuditObserver<TSaga> : IStateObserver<TSaga>
    where TSaga : class, SagaStateMachineInstance
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;

    public SagaTransitionAuditObserver(
        IServiceScopeFactory scopeFactory,
        ILogger<SagaTransitionAuditObserver<TSaga>> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // KNOWN: audit rows written in separate transaction — phantom rows possible on rollback. Acceptable for observability.
    public async Task StateChanged(BehaviorContext<TSaga> context, State currentState, State previousState)
    {
        var fromState = previousState?.Name ?? "Initial";
        var toState = currentState?.Name ?? "unknown";
        var correlationId = context.Saga.CorrelationId;
        var sagaType = typeof(TSaga).Name;

        // Attempt to extract the initiating identity from MassTransit message headers.
        // The "UserId" header is set by the BFF/API layer for user-initiated transitions.
        // For scheduled events and internal saga-to-saga transitions no header is present.
        string? initiatedBy = null;
        try { initiatedBy = context.Headers.Get<string>("UserId"); }
        catch { /* header not present or unreadable — leave null */ }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = ResolveAuditDbContext(scope.ServiceProvider);
            if (dbContext is not null)
            {
                dbContext.Set<SagaTransitionAuditEntry>().Add(new SagaTransitionAuditEntry
                {
                    SagaType = sagaType,
                    CorrelationId = correlationId,
                    FromState = fromState,
                    ToState = toState,
                    InitiatedBy = initiatedBy,
                });
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to write saga audit: {SagaType} {CorrelationId} {FromState} -> {ToState}",
                sagaType, correlationId, fromState, toState);
        }
    }

    private static DbContext? ResolveAuditDbContext(IServiceProvider sp) =>
        sp.GetServices<DbContext>()
          .FirstOrDefault(ctx => ctx.Model.FindEntityType(typeof(SagaTransitionAuditEntry)) is not null);
}
