# Search — decoupling refactor (modify existing service)

**Mode:** modify-existing-service. `WAVE_MODE=modify`.

**Goal:** drop Search's compile-time knowledge of Catalog event types. After this lands, `using Haworks.Contracts.Catalog;` should not appear under `src/Search/`. `scripts/check-architecture.sh` should report 0 warnings for Search.

**Why this matters:** see `docs/architecture/cross-cutting-coupling-audit.md` § Search. Search currently has typed consumers (`CategoryUpdatedConsumer`, `ProductCacheInvalidatedConsumer`) that hard-code Catalog event names. A search service should be reusable by any project that wants full-text indexing — the consumer should be event-shape-agnostic.

## Current state

`src/Search/Search.Application/Consumers/`:
- `CategoryUpdatedConsumer.cs` — `IConsumer<CategoryUpdatedEvent>` from `Haworks.Contracts.Catalog`
- `ProductCacheInvalidatedConsumer.cs` — `IConsumer<ProductCacheInvalidatedEvent>` from same

Both consumers do similar things: receive an event referencing some entity, then re-index that entity in Meilisearch.

## Target state

A single generic `IndexableEntityChangedConsumer<TEvent> : IConsumer<TEvent> where TEvent : IDomainEvent`. At runtime, it consults an `IIndexableEventRegistry` to decide:
- Does this event signal a re-index? (event-type FQN string lookup)
- If yes: which Meilisearch index? Which entity-id field? Which document-source repository?

Registry config lives in DB (`search_index_event_registrations` table) so adding new indexable events is config, not code:

```
search_index_event_registrations
├── id                  uuid   PK
├── event_type          text   UNIQUE   (e.g. "Haworks.Contracts.Catalog.ProductCacheInvalidatedEvent")
├── index_name          text             ("products", "categories")
├── entity_id_path      text             (jsonpath into payload to entity id)
├── action              text             ("upsert", "delete")
└── enabled             bool
```

MassTransit registration: at startup, scan the loaded `IDomainEvent` types in the message bus, and dynamically register the generic consumer for each event-type-FQN that has a row in `search_index_event_registrations`. (MassTransit supports `IConsumer<>` open-generic registration.)

## Track decomposition (3 parallel tracks)

### Track T1: registry table + repository
- EF migration: `search_index_event_registrations` table per § Target.
- `SearchIndexEventRegistration` entity in `Search.Domain/`.
- `IIndexableEventRegistry` + repository impl with 60s in-memory cache.
- Done: `dotnet test tests/Search.Integration --filter "FullyQualifiedName~IndexableEventRegistry"`.

### Track T2: generic consumer + dynamic MassTransit registration
- New `Search.Application/Consumers/IndexableEntityChangedConsumer.cs`:
  - Generic on `TEvent : IDomainEvent`
  - Looks up registration by `typeof(TEvent).FullName`
  - If registered + enabled: extract entity id via configured path, call appropriate `ISearchIndexer.UpsertAsync` or `DeleteAsync`
  - If not registered: log + drop (no error — accommodates events that flow but aren't search-relevant)
- Modify `Search.Infrastructure/DependencyInjection.cs`:
  - At startup, query the registry for all `event_type` strings
  - For each, resolve the corresponding C# type via `Type.GetType()` and register `IConsumer<T>` dynamically with MassTransit
- Done: `dotnet test tests/Search.Unit --filter "FullyQualifiedName~IndexableEntityChangedConsumer"`.

### Track T3: delete typed consumers + seed migration
- Delete `CategoryUpdatedConsumer.cs`, `ProductCacheInvalidatedConsumer.cs` and their tests.
- Seed migration: INSERT rows into `search_index_event_registrations` for both events to preserve current behavior:
  - `Haworks.Contracts.Catalog.CategoryUpdatedEvent` → `categories` index, action upsert
  - `Haworks.Contracts.Catalog.ProductCacheInvalidatedEvent` → `products` index, action upsert
- Integration test: publish a `ProductCacheInvalidatedEvent` (test assembly may import it), assert the document is updated in Meilisearch.
- Done: `bash scripts/check-architecture.sh` shows 0 warnings for Search.

## Reference files
- `src/Search/Search.Application/Consumers/CategoryUpdatedConsumer.cs` (typed consumer being replaced — read to understand current behavior)
- `src/Search/Search.Application/Consumers/ProductCacheInvalidatedConsumer.cs` (same)
- `src/Search/Search.Infrastructure/DependencyInjection.cs` (where MassTransit registration happens; you'll modify this)

## Done check
```
dotnet test tests/Search.Integration tests/Search.Unit -c Release --nologo
bash scripts/check-architecture.sh    # 0 warnings for Search
```
