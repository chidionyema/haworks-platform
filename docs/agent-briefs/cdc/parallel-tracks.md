```
REPO=/Users/chidionyema/Documents/code/ritualworks-platform
GH_REPO=chidionyema/ritualworks-platform
WAVE_MODE=modify
BASE_BRANCH=feat/cdc
BRIEF_FILE=docs/agent-briefs/cdc/parallel-tracks.md
TRACK_PREFIX=feat/cdc-
TRACKS=(T1 T2 T3 T4 T5 T6 T7)
WORKTREE_PARENT=/Users/chidionyema/.gemini/tmp/ritualworks-platform
```

# CDC Service — parallel tracks (Mode B brief)

This brief drives the parallel implementation of the Change Data Capture (CDC) pipeline.
It follows the design in `docs/agent-briefs/cdc-service-spec.md`.

---

## Universal rules

### File-scope discipline
Each track owns a disjoint set of files. **You do not touch files outside your track's "Files you own" list.** 
If you need to cross-reference a type from another track, assume it exists or use a TODO.

### No exploration
Do not grep, find, or ls outside your Files-you-own list unless required for compilation. The brief has the exact paths you need.

### No preamble
Execute, don't narrate. No 'let me first understand' prose. Output text is limited to: progress signals (one line per file group), error reports, final Done-Report. Internal reasoning stays internal.

### No scope creep
No tangential edits. If you notice an obvious improvement outside your listed file paths, write a // TODO(track-Tn) comment in YOUR file and continue. Do not refactor unrelated code.

---

## Anti-stuck

- **60-second decision time-box.** Mirror the reference file and move on.
- **Cross-track need? `// TODO(cdc-<TRACK>): <reason>` and continue.**
- **Spec ambiguous?** Pick the simpler option, add a `// TODO`, proceed.
- **No questions to user.** Operator is not in session.

---

## Reference file

When in doubt about consumer shape, mirror `src/Search/Search.Application/Consumers/CategoryUpdatedConsumer.cs`.
For service shape, mirror `src/Notifications/`.

---

### Track T1: CDC Core Relay

**Files you own (exclusive):**
- `src/Cdc/Cdc.Infrastructure/Replication/**`
- `src/Cdc/Cdc.Domain/**`
- `src/Contracts/Cdc/EntityChangedEvent.cs`

**Files you may NOT touch:**
- `src/Cdc/Cdc.Api/**`
- `src/BuildingBlocks/**`

**Reference to mirror:** `src/Contracts/IDomainEvent.cs`

**Reference (inline excerpt):**
```csharp
namespace Haworks.Contracts;
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }
}
```

**NuGet (if any):** `Npgsql`

**Done:** `dotnet build src/Cdc/Cdc.Infrastructure/Cdc.Infrastructure.csproj`

**Work plan:**
1. **Contract:** Create `src/Contracts/Cdc/EntityChangedEvent.cs` per spec § 3.
2. **Relay Loop:** Implement `PostgresLogicalReplicationSubscriber` using `Npgsql.Replication`.
3. **Decoder:** Implement `PgOutputDecoder` to map WAL records to `EntityChangedEvent`.

---

### Track T2: Admin API & CLI

**Files you own (exclusive):**
- `src/Cdc/Cdc.Api/**`
- `src/Cdc/Cdc.Application/**`
- `scripts/cdc.sh`

**Files you may NOT touch:**
- `src/Cdc/Cdc.Infrastructure/**`

**Reference to mirror:** `src/Notifications/Notifications.Api/Controllers/NotificationsController.cs`

**Reference (inline excerpt):**
```csharp
[ApiController]
[Route("api/[controller]")]
public class NotificationsController : ControllerBase { ... }
```

**NuGet (if any):** none

**Done:** `dotnet build src/Cdc/Cdc.Api/Cdc.Api.csproj && ls scripts/cdc.sh`

**Work plan:**
1. **Controller:** Implement `CdcController` with status, pause, resume, and resync endpoints (§ 5.3).
2. **CLI:** Create `scripts/cdc.sh` as a CLI wrapper for the Admin API.
3. **DI:** Wire up the API and Application layers in `DependencyInjection.cs`.

---

### Track T3: Postgres Infrastructure & Publications

**Files you own (exclusive):**
- `infra/stateful/cdc-publications/**`
- `infra/stateful/postgres-clusters/**`

**Files you may NOT touch:**
- `src/Cdc/**`

**Reference to mirror:** `infra/stateful/postgres-clusters/catalog.yaml` (if exists) or similar k8s/fly config.

**NuGet (if any):** none

**Done:** `ls infra/stateful/cdc-publications/catalog.sql`

**Work plan:**
1. **Config:** Update `infra/stateful/postgres-clusters/*.yaml` for `wal_level=logical`.
2. **Publications:** Write SQL scripts for all service databases per § 7.1.

---

### Track T4: Search Consumer Migration

**Files you own (exclusive):**
- `src/Search/Search.Application/Consumers/IndexableEntityChangedConsumer.cs`
- `src/Search/Search.Application/DependencyInjection.Cdc.cs`

**Files you may NOT touch:**
- `src/Search/Search.Application/Consumers/CategoryUpdatedConsumer.cs` (reference only)

**Reference to mirror:** `src/Search/Search.Application/Consumers/CategoryUpdatedConsumer.cs`

**Reference (inline excerpt):**
```csharp
public sealed class CategoryUpdatedConsumer : IConsumer<CategoryUpdatedEvent>
{
    private readonly ISearchIndex _index;
    public async Task Consume(ConsumeContext<CategoryUpdatedEvent> context) { ... }
}
```

**NuGet (if any):** none

**Done:** `dotnet test tests/Search.Integration/Search.Integration.csproj --filter "FullyQualifiedName~IndexableEntityChanged"`

**Work plan:**
1. **Consumer:** Implement `IndexableEntityChangedConsumer` consuming `EntityChangedEvent`.
2. **Wiring:** Register the consumer in `DependencyInjection.Cdc.cs`.

---

### Track T5: BffWeb Cache Invalidator

**Files you own (exclusive):**
- `src/BffWeb/BffWeb.Application/Consumers/CacheInvalidatorConsumer.cs`
- `src/BffWeb/BffWeb.Application/DependencyInjection.Cdc.cs`
- `infra/apps/cache-invalidator/rules.yaml`

**Files you may NOT touch:**
- `src/Search/**`

**Reference to mirror:** `src/Notifications/Notifications.Application/Consumers/`

**NuGet (if any):** none

**Done:** `dotnet test tests/BffWeb.Integration/BffWeb.Integration.csproj --filter "FullyQualifiedName~CacheInvalidator"`

**Work plan:**
1. **Consumer:** Implement `CacheInvalidatorConsumer` with config-driven rules (§ 6.1).
2. **Config:** Create the initial `rules.yaml` for product and category invalidation.

---

### Track T6: Audit Data-Mode Consumer

**Files you own (exclusive):**
- `src/Audit/Audit.Application/Consumers/DataAuditConsumer.cs`
- `src/Audit/Audit.Application/DependencyInjection.DataAudit.cs`
- `src/Audit/Audit.Infrastructure/Persistence/Migrations/*_AddDataAuditEvents.cs`

**Files you may NOT touch:**
- `src/Cdc/**`

**Reference to mirror:** `src/Audit/Audit.Application/Consumers/AuditConsumer.cs` (if exists)

**NuGet (if any):** none

**Done:** `dotnet test tests/Audit.Integration/Audit.Integration.csproj --filter "FullyQualifiedName~DataAudit"`

**Work plan:**
1. **Migration:** Create the EF migration for `data_audit_events`.
2. **Consumer:** Implement `DataAuditConsumer` to store raw payloads.

---

### Track T7: Observability & E2E

**Files you own (exclusive):**
- `infra/addons/grafana-dashboards/cdc.json`
- `tests/E2E/Journeys/CdcEndToEndJourney.cs`
- `src/Webhooks/Webhooks.Application/Consumers/CdcScaffoldConsumer.cs`
- `src/Analytics/Analytics.Application/Consumers/CdcScaffoldConsumer.cs`

**Files you may NOT touch:**
- `src/Cdc/**`
- `src/Search/**`
- `src/Audit/**`

**Reference to mirror:** `tests/E2E/Journeys/CheckoutJourney.cs` (if exists)

**NuGet (if any):** none

**Done:** `ls tests/E2E/Journeys/CdcEndToEndJourney.cs && ls src/Webhooks/Webhooks.Application/Consumers/CdcScaffoldConsumer.cs`

**Work plan:**
1. **Dashboard:** Create the Grafana dashboard JSON per § 8.2.
2. **E2E Journey:** Implement the full propagation test per § 10.5.
3. **Scaffolds:** Create the minimal consumer implementations for Webhooks and Analytics (§ 11 T7).
