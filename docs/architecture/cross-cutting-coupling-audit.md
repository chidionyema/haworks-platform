# Cross-cutting services — coupling audit

**As of:** initial inventory captured by `scripts/check-architecture.sh`.
**Companion check:** `.github/workflows/architecture.yml` — runs the same checks on every PR.

This is the inventory of where each cross-cutting service is currently coupled to specific domain logic, scored against the five forms of coupling that break reusability. Each row is the snapshot today and the move that decouples it.

## Coupling rubric (recap)

A service is "truly reusable" if you can copy its `src/<Service>/` + tests into a new repo, point it at a different RabbitMQ + Postgres + Identity issuer, and have it run with no edits. The five forms of coupling that break that test:

1. **Type-coupling to specific business entities** — `IConsumer<OrderCreatedEvent>` instead of `IConsumer<IDomainEvent>`.
2. **Knowing the shape of someone else's database** — querying a sibling's tables directly.
3. **Hardcoding the shape of events from other services** — referencing `Haworks.Contracts.Catalog.ProductCacheInvalidatedEvent` by C# type.
4. **Sharing a `Contracts` assembly that grows monotonically** — every cross-service event lives in a single shared assembly.
5. **Sharing a `BuildingBlocks` assembly via project reference** — repo-scoped vs versioned-NuGet.

## Per-service inventory

### Audit (`src/Audit/`)

| Form | Status | Detail |
|---|---|---|
| 1. Type-coupling | **PARTIAL** | `ReflectionAuditExtractor<T>` is the abstract path (works for any `IDomainEvent`). But per-event extractors (`ProductCacheInvalidatedExtractor`, `StockReservationFailedExtractor`, `VaultRotationStageExtractor`) are typed against specific Catalog/Identity events — type coupling. |
| 2. DB shape | ✅ clean | own `audit` DB, own `AuditDbContext`, no cross-service DB reads |
| 3. Event-type imports | ⚠ COUPLED | `using Haworks.Contracts.Catalog;` + `using Haworks.Contracts.Identity;` in `Application/Extraction/*.cs` — knows specific Catalog + Identity event names |
| 4. Contracts assembly | ⚠ shared | references `Contracts.csproj`; pulls in everything |
| 5. BuildingBlocks | ⚠ project-ref | references `BuildingBlocks.csproj` directly |

**Files where coupling lives:**
- `src/Audit/Audit.Application/Extraction/ProductCacheInvalidatedExtractor.cs`
- `src/Audit/Audit.Application/Extraction/StockReservationFailedExtractor.cs`
- `src/Audit/Audit.Application/Extraction/VaultRotationStageExtractor.cs`
- `src/Audit/Audit.Application/Extraction/ExtractorRegistry.cs` (registers the typed ones)

**Decoupling move (estimated effort: medium):**
1. Drop the per-event extractor classes. Lean entirely on `ReflectionAuditExtractor<T>` + per-event JSON config (entity-id field path, secret-redaction rules, etc.).
2. Remove `using Haworks.Contracts.Catalog;` and `using Haworks.Contracts.Identity;`.
3. Generic config in `audit_event_extractor_overrides` table (jsonb): `{event_type, entity_id_path, redact_paths[]}`.
4. Audit becomes purely event-shape-agnostic — drop into any project, configure overrides per-deployment.

---

### Notifications (`src/Notifications/`)

| Form | Status | Detail |
|---|---|---|
| 1. Type-coupling | ✅ clean | consumers operate on `NotificationRequestedEvent` (own type), not foreign business events |
| 2. DB shape | ✅ clean | own `notifications` DB |
| 3. Event-type imports | ✅ clean | no `using Haworks.Contracts.<Other>;` statements (verified by `check-architecture.sh`) |
| 4. Contracts assembly | ⚠ shared | references `Contracts.csproj` (only for `NotificationRequestedEvent` published from there) |
| 5. BuildingBlocks | ⚠ project-ref | direct reference |

**Coupling severity: LOW.** Notifications is the model of decoupled cross-cutting service in this stack. The only remaining work is structural (4, 5).

**Decoupling move (small):** when `Contracts` is split per-service (form 4 platform-wide fix), Notifications keeps its own message contract published from its own assembly.

---

### Payments (`src/Payments/`)

| Form | Status | Detail |
|---|---|---|
| 1. Type-coupling | ✅ clean | only consumes own subdomain types |
| 2. DB shape | ✅ clean | own `payments` DB |
| 3. Event-type imports | ✅ clean | only `using Haworks.Contracts.Payments;` (own subdomain) |
| 4. Contracts assembly | ⚠ shared | same as everywhere |
| 5. BuildingBlocks | ⚠ project-ref | same as everywhere |

**Coupling severity: LOW.** Same shape as Notifications.

**Caveat:** Payments references `Order` semantically — payment intents have an `order_id` field. That's a `string` in the API, not a typed reference, so it's logical coupling not structural. Decoupling move: rename `order_id` → `purchase_id` in the public API to make the abstraction explicit.

---

### Search (`src/Search/`)

| Form | Status | Detail |
|---|---|---|
| 1. Type-coupling | ⚠ COUPLED | `CategoryUpdatedConsumer`, `ProductCacheInvalidatedConsumer` are typed against specific Catalog events |
| 2. DB shape | ✅ clean | reads from Meilisearch (own index) |
| 3. Event-type imports | ⚠ COUPLED | `using Haworks.Contracts.Catalog;` |
| 4. Contracts assembly | ⚠ shared | same |
| 5. BuildingBlocks | ⚠ project-ref | same |

**Files where coupling lives:**
- `src/Search/Search.Application/Consumers/CategoryUpdatedConsumer.cs`
- `src/Search/Search.Application/Consumers/ProductCacheInvalidatedConsumer.cs`
- `src/Search/Search.Infrastructure/DependencyInjection.cs` (registers them)

**Decoupling move (medium):** generalize the consumers — a single `IndexableEntityChangedConsumer<T>` that handles "entity X changed, reindex it" via a configured `EntityType → IndexName` mapping. The per-event consumers go away; the index population logic is config-driven.

This is the same shape as the Audit fix (registry + reflection vs typed handlers).

---

### Content (`src/Content/`)

| Form | Status | Detail |
|---|---|---|
| 1. Type-coupling | ✅ clean | API takes `entity_type + entity_id` strings, not typed entities |
| 2. DB shape | ✅ clean | own `content` DB |
| 3. Event-type imports | ✅ clean | no foreign Contracts imports |
| 4. Contracts assembly | ⚠ shared | same |
| 5. BuildingBlocks | ⚠ project-ref | same |

**Coupling severity: LOWEST.** Content is the most reusable cross-cutting service in the stack today. Drop-in into any project that needs blob storage + virus scan + signed URLs.

---

### Identity (`src/Identity/`)

| Form | Status | Detail |
|---|---|---|
| 1. Type-coupling | ✅ clean | own JWKS + user model |
| 2. DB shape | ✅ clean | own `identity` DB |
| 3. Event-type imports | ✅ clean | only references its own subdomain (`Haworks.Contracts.Identity.VaultRotationStageEvent`) |
| 4. Contracts assembly | ⚠ shared | same |
| 5. BuildingBlocks | ⚠ project-ref | same |

**Coupling severity: LOWEST.** Like Content, Identity is genuinely reusable today modulo platform-wide fixes (4, 5).

---

## Summary

| Service | Form 1 | Form 3 | Severity | Reusable today? |
|---|---|---|---|---|
| Audit          | ⚠ partial | ⚠ Catalog+Identity | Medium | Mostly — drop the typed extractors |
| Notifications  | ✅ | ✅ | Low | Yes |
| Payments       | ✅ | ✅ (own only) | Low | Yes (rename order_id → purchase_id) |
| Search         | ⚠ typed consumers | ⚠ Catalog | Medium | No — generalize the consumers first |
| Content        | ✅ | ✅ | Low | Yes |
| Identity       | ✅ | ✅ (own only) | Low | Yes |

**The two services with real coupling debt: Audit + Search.** Both have the same shape (typed handlers against specific events from another service) and the same fix (a generic registry + reflection-based consumer).

## What the architecture check enforces

`scripts/check-architecture.sh` (run by `.github/workflows/architecture.yml`):

- **Hard fail** (blocks PR): any cross-service project reference. Today: 0 violations.
- **Soft warn** (visible but doesn't block): `using Haworks.Contracts.<OtherSubdomain>` in a cross-cutting service. Today: 2 warnings (Audit, Search).

Tighten the soft-warn rule to hard-fail once Audit + Search are refactored.

## Recommended sequencing

1. **Now:** ship the `architecture.yml` check (this PR). Ratchet for new code.
2. **Next:** refactor Audit's per-event extractors → reflection + JSON config. Run the check, expect Audit row to flip to ✅.
3. **Then:** refactor Search's per-event consumers → generic `IndexableEntityChangedConsumer<T>`. Same pattern.
4. **Later (platform-wide, separate spec):** split `Contracts` per-service, package `BuildingBlocks` as a versioned NuGet. Resolves form 4 + 5 for everyone.
