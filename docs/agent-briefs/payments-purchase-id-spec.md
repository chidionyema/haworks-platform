# Payments — `OrderId` → `PurchaseId` rename (modify existing service)

**Mode:** modify-existing-service. `WAVE_MODE=modify`.

**Goal:** make Payments' "what's being paid for" field domain-neutral. The current name `OrderId` implies Payments only ever pays for orders — but for reuse in non-commerce contexts (subscriptions, donations, deposits, services), the abstraction needs to be `PurchaseId`. Internal aliasing keeps existing callers working.

**Why this matters:** see `docs/architecture/cross-cutting-coupling-audit.md` § Payments. Severity is low (it's a logical rename, not a structural fix), but it removes the last commerce-specific assumption from the Payments service surface.

## Current state

19+ files in `src/Payments/` reference `order_id` / `OrderId`:
- Domain: `Payment.cs` aggregate, `IPaymentRepository.cs`
- Infrastructure: `PaymentDbContext.cs` schema (column `order_id`), `PaymentRepository.cs`, Stripe integration (metadata field "OrderId"), PayPal integration
- Migrations: 2 existing migrations declare the `order_id` column
- Application: `PaymentSessionRequestedConsumer.cs`, webhook handlers, interfaces

## Target state

Public API exposes `purchase_id`. Internally and on the wire (Stripe metadata, DB column), value continues to flow under `order_id` for backwards-compat — full DB rename comes later as Phase 2 once all callers migrate.

```
PUBLIC API (DTOs, controllers, OpenAPI):
  purchase_id  ← preferred, all new clients use this
  order_id     ← deprecated alias; accepted on input, NOT emitted on output

INTERNAL (Domain, EF, Stripe metadata, MassTransit messages):
  OrderId / order_id   ← unchanged in this phase

Domain entity:
  Payment.PurchaseId { get; }  ← new property, returns OrderId value (computed)
  Payment.OrderId    { get; }  ← preserved for internal use, marked [Obsolete] for new code
```

Outbound events (`Haworks.Contracts.Payments.*`): emit BOTH fields during the deprecation window. Consumers should read `purchase_id`; readers of `OrderId` continue to work.

## Track decomposition (3 parallel tracks)

### Track T1: domain + DTO surface
- `Payment.cs` — add `PurchaseId` computed property: `public Guid PurchaseId => OrderId;`. Mark `OrderId` `[Obsolete("Use PurchaseId — order_id is being renamed.")]` on PUBLIC consumers (do NOT obsolete it on internal Stripe/PayPal metadata mappers).
- All `*PaymentRequest`, `*PaymentResponse`, `*RefundRequest` DTOs: add `PurchaseId` property; on input, accept either field (`OrderId` as fallback if `PurchaseId` not provided); on output, emit `PurchaseId` only.
- API controllers: route handlers expose `purchase_id` in OpenAPI.
- Done: `dotnet test tests/Payments.Unit --filter "FullyQualifiedName~PurchaseIdAlias"` passes; existing tests still pass.

### Track T2: outbound events
- Modify `Haworks.Contracts.Payments.*Event` records (in `src/Contracts/`) — add `PurchaseId` field next to existing `OrderId`. Both populated on emit.
- Update event publishers in `Payments.Application/Consumers/` and `Payments.Infrastructure/Stripe/` to set both.
- Done: integration test asserts both fields present on emitted events.

### Track T3: documentation + deprecation timeline
- New file `docs/runbooks/payments-purchase-id-migration.md`:
  - When `OrderId` is removed from DTOs and events (target: 2 releases out)
  - How callers should migrate (read `purchase_id`, send either)
  - DB column rename (Phase 2 — not in this PR)
- Update `payments-port-from-monolith.md` and `payments-port-phase-2.md` if they reference `OrderId`.
- Done: docs PR-checked, no other tests required.

## Out of scope (deferred to Phase 2)
- DB column rename: `payments.order_id` → `payments.purchase_id`. Requires:
  - Migration (rename column or copy + dual-write)
  - Stripe webhook compatibility (existing in-flight payments have `OrderId` in their stored metadata — must keep reading both)
  - Coordinated rollout with all consumers of `OrderId`
- Renaming `IPaymentRepository.GetByOrderIdAsync` → `GetByPurchaseIdAsync` (internal API; do this in Phase 2 alongside DB rename)

## Reference files
- `src/Payments/Payments.Domain/Payment.cs` (aggregate where the new property lives)
- `src/Payments/Payments.Application/Consumers/PaymentSessionRequestedConsumer.cs` (DTO mapping pattern)
- `src/Contracts/Payments/` (event records to dual-emit from)

## Done check
```
dotnet test tests/Payments.Unit tests/Payments.Integration -c Release --nologo
bash scripts/check-architecture.sh
# Existing tests still pass; new PurchaseIdAlias tests pass; check-architecture
# unchanged (this rename doesn't affect coupling-audit metrics — it's UX, not
# structural decoupling).
```
