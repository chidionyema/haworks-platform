# MassTransit Saga Operations Guide

> Staff-level engineering standards for saga reliability, observability, and failure recovery.  
> Last updated: 2026-05-19

---

## Architecture Decision: Saga Middleware Pipeline Order

The order of MassTransit middleware filters determines whether retries work or poison the system.

```
CORRECT pipeline (retry outermost):

  Message → UseMessageRetry → UseEntityFrameworkOutbox → Saga Logic
                ↑                                            |
                |__________ fresh DI scope on retry _________|

WRONG pipeline (retry inside outbox):

  Message → UseEntityFrameworkOutbox → UseMessageRetry → Saga Logic
                                           ↑                |
                                           |__ SAME poisoned DbContext __|
```

**Why it matters**: When a saga INSERT fails (unique constraint, concurrency), the DbContext change tracker is poisoned. If retry reuses the same DbContext, EF Core throws `InvalidOperationException: entity already tracked` on every subsequent attempt. The message exhausts retries and dead-letters — silently.

**Implementation**: `BoundedContextSagaDefinition<TSaga, TDbContext>` in BuildingBlocks:

```csharp
protected override void ConfigureSaga(
    IReceiveEndpointConfigurator endpointConfigurator,
    ISagaConfigurator<TSaga> sagaConfigurator,
    IRegistrationContext context)
{
    // 1. Retry MUST be outermost — fresh DI scope on each attempt
    endpointConfigurator.UseMessageRetry(r =>
    {
        r.Interval(5, TimeSpan.FromMilliseconds(500));
        r.Handle<DbUpdateException>();
        r.Handle<DbUpdateConcurrencyException>();
    });

    // 2. Outbox/EF integration INSIDE retry
    endpointConfigurator.UseEntityFrameworkOutbox<TDbContext>(context);
}
```

---

## Golden Rules

### 1. Sagas Are Black Boxes — No Domain Queries

**NEVER** query saga state tables from command handlers, controllers, or MediatR handlers.

```csharp
// WRONG — poisons the DbContext, causes tracking conflicts
var existing = await db.CheckoutSagas
    .Where(s => s.IdempotencyKey == key)
    .FirstOrDefaultAsync(ct);

// RIGHT — use a separate IdempotencyJournal table or MassTransit's InboxState
```

**Why**: The command handler and the saga consumer share the same scoped `DbContext` (via `ExistingDbContext<T>()`). If the handler loads a saga entity into the change tracker, and MassTransit's saga repository later tries to `Add` a new instance with the same `CorrelationId`, EF Core throws:

> `InvalidOperationException: The instance of entity type 'CheckoutSagaState' cannot be tracked because another instance with the same key value for {'CorrelationId'} is already being tracked.`

**Enforcement**: Architecture guard `Saga_state_queries_in_command_handlers_use_AsNoTracking` in `PlatformGuardTests.cs`.

### 2. Never Inject IBus in Command Handlers

```csharp
// WRONG — IBus is a singleton, bypasses the EF outbox entirely
internal sealed class StartCheckoutHandler(IBus bus, ICheckoutDbContext db) { ... }

// RIGHT — IPublishEndpoint is scoped and outbox-aware
internal sealed class StartCheckoutHandler(IPublishEndpoint publish, ICheckoutDbContext db) { ... }
```

**Why**: `IBus.Publish()` goes directly to RabbitMQ. If `SaveChangesAsync()` later fails, you've emitted a ghost event. `IPublishEndpoint` with `UseBusOutbox()` writes to the outbox table inside the same EF transaction — atomic with business data.

**Enforcement**: Architecture guard `Command_handlers_must_not_inject_IBus` in `PlatformGuardTests.cs`.

### 3. No xmin Concurrency Tokens

```csharp
// WRONG — broken under Npgsql 9 (OID 23 vs 28 mismatch)
entity.Property<uint>("xmin")
    .HasColumnType("xid")
    .IsConcurrencyToken();

// RIGHT — MassTransit's Version property + UsePostgres() pessimistic locks
// (Version is auto-managed by ISagaVersion, no manual config needed)
```

**Why**: Npgsql 9.x sends `xmin` parameters with PostgreSQL OID 23 (integer) instead of 28 (xid). PostgreSQL has no implicit cast → every UPDATE with `xmin` in the WHERE clause matches 0 rows → `DbUpdateConcurrencyException`. This breaks ALL saga state transitions.

**Enforcement**: Architecture guard `No_xmin_concurrency_tokens_in_DbContexts` in `PlatformGuardTests.cs`.

### 4. Fault Consumers for Compensating Transactions

When a saga exhausts all retries, MassTransit publishes `Fault<T>`. Without a fault consumer, the failure is invisible (only the `_error` queue grows).

```csharp
public sealed class CheckoutFaultConsumer : IConsumer<Fault<CheckoutInitiatedEvent>>
{
    public async Task Consume(ConsumeContext<Fault<CheckoutInitiatedEvent>> context)
    {
        var sagaId = context.Message.Message.SagaId;
        _logger.LogCritical("Checkout saga {SagaId} failed permanently: {Exception}",
            sagaId, context.Message.Exceptions.FirstOrDefault()?.Message);

        // Compensate: cancel payment intent, release stock, notify user
        await _notificationService.NotifyOpsAsync($"Saga {sagaId} dead-lettered");
    }
}
```

### 5. Error Queue = SLO Breach

The `_error` queue is NOT an operational dashboard. Any message there means the system breached its SLO.

**Required monitoring**:
- Alert on `_error` queue depth > 0
- Dashboard showing error queue growth rate
- Runbook for each `_error` queue (what to investigate, how to replay)

**Replaying dead letters**: Use RabbitMQ Shovel or the MassTransit management API to move messages from `checkout-saga-state_error` back to `checkout-saga-state` after the root cause is fixed.

### 6. Pessimistic Locking for Sagas

```csharp
mt.AddSagaStateMachine<CheckoutSaga, CheckoutSagaState, CheckoutSagaDefinition>()
    .EntityFrameworkRepository(r =>
    {
        r.ExistingDbContext<CheckoutDbContext>();
        r.UsePostgres();  // SELECT ... FOR UPDATE — serializes concurrent access
    });
```

**Why**: Optimistic concurrency (RowVersion) causes massive retry spikes under load. Pessimistic locking (`FOR UPDATE`) serializes access at the database level — one consumer processes at a time, no retries needed for concurrency.

---

## Observability Stack

### Three-Layer Diagnostic Observers

| Observer | Fires When | Log Level | Purpose |
|----------|-----------|-----------|---------|
| `DiagnosticRetryObserver` | Every retry attempt | WARNING | Immediate visibility into transient failures |
| `DiagnosticConsumeObserver` | Message dead-lettered | ERROR | Final consumer fault after all retries exhausted |
| `DiagnosticReceiveObserver` | Message undeliverable | CRITICAL | Deserialization failures, routing errors |

All three are registered via `AddMassTransitDiagnostics()` and wired in `ConfigureStandardRabbitMq()`.

### What Was Missing (2026-05-19 Incident)

- 191 messages in `checkout-saga-state_error` — zero application logs
- 1,719 messages in `payment-session-requested_error` — zero application logs
- All 3 saga tables had 0 rows — zero alerts

**Root causes**:
1. `IConsumeObserver.ConsumeFault` only fires after ALL retries (up to 36 min)
2. During the retry window, failures are completely silent
3. No error queue depth monitoring existed

**Fix applied**: `DiagnosticRetryObserver` logs every retry attempt immediately.

---

## Bug Catalog (Reference)

| Bug | Root Cause | Symptom | Fix |
|-----|-----------|---------|-----|
| xmin OID mismatch | Npgsql 9 sends OID 23 not 28 | All saga UPDATEs fail silently | Remove xmin, use Version |
| DbContext tracking conflict | Retry inside outbox scope | `InvalidOperationException` → dead letter | Retry outside outbox |
| Saga query poisoning | Command handler queries saga table | Same tracking conflict | Remove query, use InboxState |
| IPublishEndpoint outside consumer | IBus/non-scoped publish | Ghost events, outbox bypass | Use scoped IPublishEndpoint |
| Missing Currency | Model binding gap | Saga throws on Initially | Add field to request model |
| Silent dead letters | No retry observer | 191 error msgs, zero logs | DiagnosticRetryObserver |

---

## Architecture Guards (CI-Enforced)

| Guard | What It Checks | Prevents |
|-------|---------------|----------|
| `No_xmin_concurrency_tokens_in_DbContexts` | No `Property<uint>("xmin").IsConcurrencyToken()` | Npgsql 9 OID bug |
| `Saga_state_queries_in_command_handlers_use_AsNoTracking` | No `.Sagas.Where()` without AsNoTracking | Tracking conflicts |
| `Command_handlers_must_not_inject_IBus` | No `IBus` in `*Command*.cs` | Outbox bypass |

---

## Incident Timeline (2026-05-19)

1. Deployed 8 services to Fly.io — 9/13 demos working
2. Checkout saga returned 202 but saga table had 0 rows
3. InboxState showed 10+ consumed messages — saga never persisted
4. Error queue had 191 messages — zero application logs
5. Root cause: EF tracking conflict from saga query + retry inside outbox scope + xmin bug
6. Fix: removed saga query, retry outside outbox, xmin removed, observers added
7. Architecture guards added to prevent recurrence
