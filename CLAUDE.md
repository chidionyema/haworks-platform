# Token Optimization Rules
- Be maximally concise. No preamble, no filler, no restating the request.
- For code tasks: output only the code change, no explanation unless asked.
- Use haiku subagents for all exploration/research tasks.
- Never read full files — always use offset/limit after Grep/Glob to target exact lines.
- Parallelize all independent tool calls.
- Don't list what you're "about to do" — just do it.
- Skip confirmation for safe, reversible actions (edits, reads, tests).

# Project
- .NET 9.0 microservices platform (Clean Architecture)
- See `.claude/projects/*/memory/` for full architecture reference

# Integration Test Rules (ENFORCED BY CI)
- NEVER create raw Testcontainers (PostgreSqlBuilder, ContainerBuilder, etc.) in test projects
- ALWAYS use shared singletons from `BuildingBlocks.Testing.Containers`:
  - `SharedTestPostgres.CreateDatabaseAsync("svc")` — standard Postgres
  - `SharedTestPostGIS.CreateDatabaseAsync("svc")` — PostGIS (geospatial)
  - `SharedTestElasticsearch.GetConnectionAsync("svc")` — Elasticsearch
- Containers use `WithReuse(true)` — one container per type across all test runs
- Containers use `WithReuse(true)` — one container per type across all test runs
- CI architecture check (`scripts/check-architecture.sh`) will FAIL on raw container usage

# Security & Edge Case Rules (MANDATORY — learned from platform audit)

## Authentication & Authorization
- EVERY controller MUST have `[Authorize]` unless explicitly documented as public
- EVERY `Program.cs` MUST call `app.UseAuthentication()` BEFORE `app.UseAuthorization()`
- NEVER take UserId/PartnerId/SellerId from request body — ALWAYS extract from JWT claims (`User.FindFirst(ClaimTypes.NameIdentifier)`)
- EVERY mutation endpoint (POST/PUT/DELETE) MUST verify the authenticated user owns the resource (IDOR check)
- Admin-only operations MUST use `[Authorize(Roles = "Admin")]`, NEVER `[AllowAnonymous]`
- Hangfire dashboards MUST use `DashboardOptions` with auth filter in non-Development environments
- SignalR hubs MUST have `[Authorize]` and verify group membership matches the authenticated user

## Domain Entity Guards
- State transition methods (MarkCompleted, MarkRefunded, Cancel, etc.) MUST validate current status before transitioning
- Calling a transition method twice MUST throw `InvalidOperationException`, never silently overwrite
- Financial amounts MUST be validated: `amount > 0` for creates, `refundAmount <= paymentAmount` for refunds
- Ledger debits MUST check `balance >= amount` before deducting (no negative balances)
- Factory methods (`Create()`) MUST validate ALL invariants — never rely solely on FluentValidation

## Validators
- Amount validators: use `GreaterThan(0)`, NEVER `GreaterThanOrEqualTo(0)` (prevents $0 orders/checkouts)
- Date validators: use `.Must(t => t > DateTimeOffset.UtcNow)`, NEVER `.GreaterThan(DateTimeOffset.UtcNow)` (the latter captures "now" at startup, not per-request)
- Email recipients: validate with `.EmailAddress()` when channel is Email
- Phone recipients: validate with E.164 regex when channel is SMS
- URLs: validate scheme (https only), block private IP ranges (SSRF protection)

## Consumers & Event Handlers
- ALWAYS use `GetByIdTrackedAsync` (not `GetByIdAsync`) when you intend to modify and save an entity
- NEVER use placeholder values (`Guid.NewGuid()` for sellerId, `NotImplementedException` for factory methods) in production code paths
- Saga compensation: ALWAYS release reserved resources on ALL failure/review paths
- Idempotency: add unique indexes on `(SubscriptionId, EventId)` or equivalent for fan-out consumers
- Kafka consumers: disable AutoCommit, commit only after successful processing

## Edge Case Testing Checklist (apply to every new feature)
- [ ] Ownership: can User A access/modify User B's resource? Test with wrong userId
- [ ] Double-call: what happens when the same operation runs twice? (idempotency)
- [ ] Invalid status: what happens when operation runs on wrong-status entity? (e.g., refund a Pending payment)
- [ ] Boundary values: zero, negative, max, one-over-max for all numeric inputs
- [ ] Concurrent access: two requests racing on the same resource (use `Task.WhenAll` in tests)
- [ ] Missing data: null/empty optional fields, missing JWT claims
- [ ] Unauthenticated: verify 401 returned for all protected endpoints
