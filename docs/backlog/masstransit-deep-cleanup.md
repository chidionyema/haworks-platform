# MassTransit & Saga Deep Cleanup — Technical Debt Cleardown

> Priority: CRITICAL — this platform handles real money  
> Status: MUST DO before any new feature work  
> Created: 2026-05-19  
> Trigger: 191 dead-lettered checkout sagas + 1,719 dead-lettered payment sessions, all silent

---

## The Problem

We don't fully understand how our own tools work. Evidence:

1. **1,910 silently dead-lettered messages** across 2 queues — zero logs, zero alerts
2. **Zero successful saga instances** across 3 saga types on production (Fly.io)
3. **5 distinct root causes** discovered by trial-and-error over 8+ hours of firefighting
4. **No fault consumers** — when a saga fails, nothing compensates (stuck payments, phantom stock holds)
5. **Inconsistent middleware ordering** across 8 consumer definitions
6. **Publish-after-SaveChanges** in 4 command handlers (lost-event windows)
7. **xmin concurrency tokens** still present in 6 services despite being broken since Npgsql 9 upgrade

This is unacceptable for a payment platform.

---

## Root Cause Summary

| # | Bug | Impact | How Long Silent |
|---|-----|--------|----------------|
| 1 | Retry inside outbox scope → poisoned DbContext | All saga retries fail with tracking conflict | Since first deploy |
| 2 | Saga state query in command handler → tracking conflict | Checkout saga never creates | Since first deploy |
| 3 | Npgsql 9 xmin OID mismatch | All saga UPDATEs fail (state transitions broken) | Since Npgsql 9 upgrade |
| 4 | Missing Currency in request model | Checkout saga throws on Initially | Since model was created |
| 5 | No retry observer → silent retry loops | 36-minute silent failure window per message | Since MassTransit adoption |
| 6 | No fault consumers → no compensation | Dead letters accumulate, no customer notification | Since saga creation |

---

## Cleanup Workstreams

### Workstream 1: Understand the Tools (1 day)

Every engineer working on this platform must understand:

1. **MassTransit message lifecycle**: Publish → Exchange → Queue → Consumer → Retry → Redelivery → Error Queue → Fault<T>
2. **EF Outbox transaction boundary**: What's inside the transaction vs outside
3. **Scoped vs singleton DI**: IPublishEndpoint (scoped, outbox-aware) vs IBus (singleton, bypasses outbox)
4. **Retry filter ordering**: Why retry must be outermost (fresh DI scope = clean DbContext)
5. **Saga state machine lifecycle**: Initially → During → Finalize, and when SaveChanges happens

**Deliverable**: Run through `docs/masstransit-saga-operations.md` as a team. Every engineer signs off.

### Workstream 2: Local Saga Smoke Test (1 day)

We cannot debug sagas by deploying to Fly.io and checking DB tables. We need:

1. **docker-compose environment** that runs: checkout + catalog + payments + RabbitMQ + Postgres
2. **Saga smoke test script** that:
   - Starts a checkout saga
   - Verifies StockReservationRequested published
   - Verifies StockReserved consumed
   - Verifies PaymentSessionRequested published
   - Verifies saga reaches Completed state
   - Runs in < 30 seconds
3. **CI integration**: saga smoke test runs on every PR that touches saga code

**Deliverable**: `make test-saga` works locally and in CI.

### Workstream 3: Fault Consumers (2-3 days)

See `docs/backlog/saga-fault-consumers.md` for the full spec.

**Phase 1** (Checkout — highest financial risk):
- `IConsumer<Fault<StockReservationRequestedEvent>>` → release stock, cancel payment
- `IConsumer<Fault<PaymentCompletedEvent>>` → auto-refund, release stock, alert ops
- Integration tests for each scenario

**Phase 2** (Refund + Subscription):
- `IConsumer<Fault<RefundRequestedEvent>>` → escalate to ops
- `IConsumer<Fault<LedgerReversalRequestedEvent>>` → flag books discrepancy
- `IConsumer<Fault<SubscriptionCancellationEvent>>` → force-cancel on Stripe

### Workstream 4: Error Queue Monitoring (0.5 day)

- Health check endpoint that queries RabbitMQ management API for `_error` queue depth
- Alert on depth > 0 (any error queue message = SLO breach)
- Dashboard in portfolio site showing error queue status
- Runbook per error queue: what to investigate, how to replay

### Workstream 5: Platform-Wide Audit & Guards (1 day)

All findings from the 2026-05-19 audit, enforced in CI:

| Guard | Status |
|-------|--------|
| No xmin concurrency tokens | ✅ Implemented |
| Saga queries use AsNoTracking | ✅ Implemented |
| No IBus in command handlers | ✅ Implemented |
| Retry outside outbox in all definitions | ✅ Fixed in code, needs guard |
| Publish before SaveChanges | Needs guard |
| Every saga has fault consumers | Needs guard |
| Error queue depth = 0 in health checks | Needs implementation |

### Workstream 6: Saga Observability (0.5 day)

- `DiagnosticRetryObserver`: ✅ Implemented — logs every retry attempt
- `DiagnosticConsumeObserver`: ✅ Implemented — logs dead-lettered faults
- `DiagnosticReceiveObserver`: ✅ Implemented — logs undeliverable messages
- **Missing**: Saga state audit trail (log every state transition with CorrelationId)
- **Missing**: Saga duration metrics (time from Initially to Completed/Abandoned)
- **Missing**: Structured log correlation (saga CorrelationId in all downstream service logs)

---

## Definition of Done

This cleanup is complete when:

1. [ ] `make test-saga` passes locally — full checkout saga completes in < 30s
2. [ ] All 3 sagas create instances on Fly.io (non-zero rows in saga tables)
3. [ ] Fault consumers exist for all critical saga events
4. [ ] Error queue depth is monitored and alerting
5. [ ] All architecture guards pass in CI
6. [ ] Team has reviewed `docs/masstransit-saga-operations.md`
7. [ ] The landing page checkout demo works end-to-end for a visitor

---

## Timeline

| Week | Workstream | Owner |
|------|-----------|-------|
| 1 | WS1 (understand tools) + WS2 (local smoke test) | — |
| 1 | WS5 (audit & guards) | — |
| 2 | WS3 Phase 1 (checkout fault consumers) | — |
| 2 | WS4 (error queue monitoring) | — |
| 3 | WS3 Phase 2 (refund + subscription) | — |
| 3 | WS6 (saga observability) | — |

---

## Lessons Learned

1. **Don't deploy what you can't test locally.** We spent 8+ hours debugging sagas by deploying to Fly.io and checking DB tables. A 30-second local smoke test would have caught all 5 bugs in the first hour.

2. **Silent failures are worse than crashes.** MassTransit's inbox/outbox can make failures invisible. Every consumer must have observability by default — not opt-in.

3. **Understand your tools before building on them.** The retry-inside-outbox ordering, the IPublishEndpoint scoping, the xmin OID mismatch — all documented in MassTransit/Npgsql changelogs. We shipped without reading them.

4. **Architecture guards aren't optional for payment systems.** The guards we added today would have caught all 6 bugs at compile time or CI time. The cost of adding them: 1 hour. The cost of not having them: 8+ hours of debugging + 1,910 lost messages.
