# Output
- No preamble, no postamble, no restating the request.
- Code tasks: output the change only. No explanation unless asked.
- After edits: one-line summary of changed files. No diff recap.
- Errors: paste the failing line + fix. Don't re-explain.
- Don't narrate what you're about to do — just do it.
- Skip confirmation for safe, reversible actions (edits, reads, tests).

# Reading
- Never read full files. Use Grep/Glob, then offset/limit to target exact lines.
- Vague exploration → spawn haiku subagent, return summary only.
- Don't read: bin/, obj/, node_modules/, .git/, **/generated/, **/*.Designer.cs, *ModelSnapshot.cs
- Don't read test fixtures > 200 lines unless asked.

# Model routing
- Default: sonnet.
- Exploration, log grep, dependency scans, file search: haiku subagent.
- Multi-file refactors, architectural calls: opus only when sonnet stalls.

# Project
- .NET 9.0 microservices, Clean Architecture.
- Architecture, security rules, audit protocol: `.claude/projects/*/memory/`
- Gemini-compatible audit brief: `docs/agent-briefs/audit-protocol.md`

# Commands
- Build: `dotnet build`
- Test project: `dotnet test path/to/Project.Tests.csproj`
- Test single: `dotnet test --filter "FullyQualifiedName~ClassName"`
- Format: `dotnet format`
- Migrations: `dotnet ef database update --project src/[Service]`

# Engineering bar
- Before adding an abstraction, check if an existing one fits.
- Before adding a dependency, check if BuildingBlocks already provides it.
- No TODOs left in committed code. No commented-out code.
- If a fix needs a workaround, flag it explicitly — don't hide it.

# Git workflow (ENFORCED — branch protection on main)
- **NEVER push directly to main.** Always use a branch + PR.
- Create a branch: `git checkout -b fix/description` or use worktrees.
- Push the branch, open a PR: `gh pr create --title "..." --body "..."`
- CI must pass (Build + unit/arch/contract) before merge.
- Max 3 services per PR. Sweeping changes must be split.
- Run `make test svc=X` locally for every service you touched before pushing.
- One agent per service at a time — don't race on the same files.
# Project
- .NET 9.0 microservices platform (Clean Architecture)
- See `.claude/projects/*/memory/` for full architecture reference
- See `.claude/projects/*/memory/security-rules.md` for mandatory security rules
- See `.claude/projects/*/memory/audit-protocol.md` for 12-lens service audit process
- See `docs/agent-briefs/audit-protocol.md` for the same protocol (Gemini agent compatible)
# Architectural Review Guidelines

When reviewing, modifying, or reading code in this repository, you must act as a Principal Enterprise Architect and Security Auditor. Do not focus solely on the "happy path." Always analyze the code against the following 12 Architectural Lenses:

1. **Data & Domain Integrity:** Bounds checking, negative balance prevention, domain invariants.
2. **Concurrency & Race Conditions:** Pessimistic/optimistic locks, unique DB indexes.
3. **Transaction Boundaries & Sagas:** Outbox patterns, rollback compensations, failure states.
4. **State Machine Correctness:** Forced/invalid transitions, stuck states.
5. **Idempotency:** Safe retries for external APIs and webhooks.
6. **Error Handling & State Leakage:** Swallowed exceptions, orphaned data.
7. **Zero-Trust Security:** Rate-limiting, strict JWT claim validation (no IDOR), magic-byte file validation, SSRF prevention.
8. **Integration & Edge Boundaries:** BFF/Gateway sanitization.
9. **Scalability:** Unbounded queries, memory leaks, N+1 issues.
10. **Testing Deficiencies:** Missing failure paths.
11. **Configuration & Secrets:** Hardcoded secrets, missing rotation.
12. **Database & Persistence Truth:** Schema-level constraints and foreign keys.
# Integration Test Rules (ENFORCED BY CI)
- NEVER create raw Testcontainers (PostgreSqlBuilder, ContainerBuilder, etc.) in test projects
- ALWAYS use shared singletons from `BuildingBlocks.Testing.Containers`:
  - `SharedTestPostgres.CreateDatabaseAsync("svc")` — standard Postgres
  - `SharedTestPostGIS.CreateDatabaseAsync("svc")` — PostGIS (geospatial)
  - `SharedTestElasticsearch.GetConnectionAsync("svc")` — Elasticsearch
- Containers use `WithReuse(true)` — one container per type across all test runs
- CI architecture check (`scripts/check-architecture.sh`) will FAIL on raw container usage

# 🏛️ Core Architectural Laws

You are an expert .NET Enterprise Architect. You are operating in a mission-critical financial system. You MUST obey the following architectural laws. Do not suggest or write code that violates these constraints.

## 1. The MassTransit Transaction Law
When writing or modifying a MassTransit `IConsumer<T>`:
* **NEVER** call `_dbContext.Database.BeginTransactionAsync()`.
* **NEVER** call `_dbContext.SaveChangesAsync()`.
* **WHY:** We use the MassTransit Entity Framework Core Inbox/Outbox. MassTransit wraps the `Consume` method in an ambient transaction and commits it automatically upon success. Manual transaction management causes nested transaction crashes or Outbox bypasses.
* **HOW:** Inherit from `MissionCriticalConsumerBase<TEvent, TDbContext>` and place DB mutations inside `ExecuteBusinessLogicAsync`.

## 2. The External I/O & Database Lock Law (Three-Phase Gateway)
When an action requires both a database state change and a call to an external API (e.g., Stripe, PayPal, HTTP calls):
* **NEVER** hold a database transaction or lock open while awaiting an external network call.
* **ALWAYS** use the Three-Phase Orchestrator Pattern:
  * **Phase 1 (Atomic):** Open a short transaction. Pessimistically lock the aggregate (`FOR UPDATE`). Mutate state to "Pending". Commit.
  * **Phase 2 (I/O):** Make the external API call wrapped in Polly resilience policies. Ensure the Idempotency Key is passed to the provider. No DB locks can exist here.
  * **Phase 3 (Atomic):** Open a short transaction. Re-lock the aggregate. Update state to "Success/Failed". Publish MassTransit Outbox events. Commit.

## 3. The Logical Idempotency Law
Network idempotency (Inbox) is not enough. You must protect against logical business duplicates.
* **NEVER** rely on catching `DbUpdateException` for unique index violations to handle race conditions. In EF Core, this poisons the `DbContext` and breaks the MassTransit Outbox.
* **ALWAYS** use Pessimistic Row Locking for concurrency.
* **HOW:** Use raw SQL `FOR UPDATE` (or `FOR UPDATE SKIP LOCKED`) to lock the business key (e.g., a row in an `IdempotencyJournal` table). If the row already exists, safely exit the consumer without throwing an exception.

## 4. Code Generation Standards
* **NO SHORTCUTS:** Never use `// TODO`, `throw new NotImplementedException()`, or `Guid.NewGuid()` as placeholders for real business logic or idempotency keys. Write complete, compiling code.
* Always inject `CancellationToken` into `ExecuteAsync`, `FirstOrDefaultAsync`, and external API calls.
  ┌──────────────────────────────────┬────────────────────────────────────┬───────┐
  │             Command              │                What                │ Time  │
  ├──────────────────────────────────┼────────────────────────────────────┼───────┤
  │ make build svc=identity          │ Build one service (no analyzers)   │ ~30s  │
  ├──────────────────────────────────┼────────────────────────────────────┼───────┤
  │ make build                       │ Build full solution (no analyzers) │ ~2min │
  ├──────────────────────────────────┼────────────────────────────────────┼───────┤
  │ make build-full                  │ Build with all 850 analyzer rules  │ ~4min │
  ├──────────────────────────────────┼────────────────────────────────────┼───────┤
  │ make lint                        │ Analyzers only                     │ ~4min │
  ├──────────────────────────────────┼────────────────────────────────────┼───────┤
  │ make test svc=identity           │ Build + test one service           │ ~45s  │
  ├──────────────────────────────────┼────────────────────────────────────┼───────┤
  │ make test svc=identity mode=unit │ Unit tests only                    │ ~15s  │
  └──────────────────────────────────┴────────────────────────────────────┴───────┘
# CI Compatibility Rules
- Before adding ANY test, verify it passes with: `dotnet test <project> --filter "FullyQualifiedName~<TestName>"`
- E2E tests (Aspire AppHost) MUST be in `tests/E2E/` — they run in a dedicated CI job, not the fast unit test step
- Integration tests MUST have "Integration" in their namespace or project name
- NEVER add tests that require infrastructure (Docker, Vault, external APIs) to unit test projects
- The fast CI step filter is: `FullyQualifiedName!~Integration&FullyQualifiedName!~E2E&FullyQualifiedName!~Smoke`

# MANDATORY: Pre-Push Verification (NEVER SKIP)
- **NEVER push to any branch without running these locally first:**
  1. `dotnet build HaworksPlatform.sln` — zero errors (excluding HWK023)
  2. `dotnet test HaworksPlatform.sln --no-build --filter "FullyQualifiedName!~Integration&FullyQualifiedName!~E2E&FullyQualifiedName!~Smoke"` — zero failures
  3. For each service touched: `dotnet test tests/{Service}/{Service}.Integration/*.csproj` — zero failures
- **If any step fails, fix it BEFORE pushing. Do not push broken code.**
- **Do not announce you will push. Run tests. Fix failures. Then push.**
- **The continuous review pipeline (Phase 4) enforces this automatically for review PRs.**
