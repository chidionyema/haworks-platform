# Platform Backlog — Production Readiness

> Last updated: 2026-05-16 | 26 services in codebase, 20 deployed

## How to use this document
- Items are ordered by priority within each tier
- **Effort**: S = days, M = 1-2 weeks, L = 1+ month
- Check off items as they're completed
- Move items between tiers as priorities shift

---

## Tier 0 — Critical (blocks production)

### B-001: Observability stack (Prometheus + Grafana + Loki)
- **Effort**: M
- **Status**: Not started
- **Why**: OpenTelemetry traces + metrics are wired in every service via `ServiceDefaults.cs`. Tempo is configured for distributed tracing. But **no Prometheus scrape configs, no Loki log aggregation, no Grafana dashboards exist**. `infra/observability/` directories contain only `.gitkeep`. `saga-alerts.yml` has 4 alert rules but no Prometheus instance to evaluate them.
- **What to do**:
  - [ ] Create `infra/observability/otel-collector/config.yaml` (OTLP receiver → Prometheus remote_write + Loki exporter)
  - [ ] Create `infra/observability/prometheus/prometheus.yml` (scrape configs for all services)
  - [ ] Create `infra/observability/loki/loki.yaml`
  - [ ] Create `infra/observability/grafana/` with datasources + provisioned dashboards
  - [ ] Add docker-compose services for the stack
  - [ ] Deploy to Fly.io or Grafana Cloud
  - [ ] Wire Alertmanager → Slack/PagerDuty for saga-alerts.yml rules
- **Dependencies**: Tempo already deployed, OTel exporters in every service
- **Risk if skipped**: Blind in production — can't detect errors, latency, or saga failures

### B-002: Deploy 6 undeployed services
- **Effort**: S (config files only)
- **Status**: Code exists, no fly.toml
- **Services**: Analytics, FeatureFlags, Media, Realtime, RulesEngine, Localization
- **What to do**:
  - [ ] Create `fly.analytics.toml`, `fly.featureflags.toml`, `fly.media.toml`, `fly.realtime.toml`, `fly.rulesengine.toml`, `fly.localization.toml`
  - [ ] Add Dockerfiles (follow existing pattern)
  - [ ] Register in `deploy/fly/deploy.sh`
  - [ ] Register in Aspire AppHost `deploy/aspire/Program.cs`
  - [ ] Set required Fly secrets per service
- **Dependencies**: Each service's DB/Redis/RabbitMQ connections
- **Risk if skipped**: 6 services with production capabilities unreachable

### B-003: Pricing service implementation
- **Effort**: L
- **Status**: README only — zero code
- **Why**: `src/Pricing/` contains only a README describing responsibilities. No domain model, no API, no migrations. Checkout saga and Catalog have no price calculation layer. Discounts, promotions, tiered pricing cannot be applied.
- **What to do**:
  - [ ] Design domain model: PricingRule, Discount, Promotion, TieredPrice
  - [ ] Create Pricing.Domain, Pricing.Application, Pricing.Infrastructure, Pricing.Api
  - [ ] Implement `CalculateEffectivePriceQuery` consumed by CheckoutOrchestrator
  - [ ] Add MassTransit consumer for `PriceQuoteRequestedEvent`
  - [ ] Integration with Catalog (product base prices) and Merchant (merchant-specific rules)
  - [ ] Publish `PriceCalculatedEvent` for audit trail
- **Dependencies**: Catalog (product prices), CheckoutOrchestrator (consumes price), Payments (amount to charge)
- **Risk if skipped**: Hardcoded prices, no discounts/promotions, no dynamic pricing

### B-004: BFF global rate limiting
- **Effort**: S (1 day)
- **Status**: Not started
- **Why**: Identity has `AddRateLimiter` for auth endpoints only. Privacy has it for GDPR endpoints. **BffWeb has zero rate limiting** — all inbound public API traffic is unthrottled.
- **What to do**:
  - [ ] Add `builder.Services.AddRateLimiter()` to BffWeb Program.cs with:
    - Per-IP sliding window (e.g., 100 req/min)
    - Per-user token bucket (e.g., 30 req/min authenticated)
    - Per-endpoint fixed window for expensive operations (saga/start, events/trigger)
  - [ ] Add `app.UseRateLimiter()` in middleware pipeline
  - [ ] Create `AddPlatformRateLimiting()` in BuildingBlocks/Extensions for reuse
  - [ ] Return standard 429 + Retry-After headers
- **Dependencies**: None
- **Risk if skipped**: DDoS, credential stuffing, API scraping on public-facing gateway

### B-005: Database backup automation
- **Effort**: M
- **Status**: Not started
- **Why**: No `pg_dump`, WAL-G, Barman, or PITR configuration anywhere. Neon Postgres (production) has its own backup, but no verification or restore testing exists.
- **What to do**:
  - [ ] Verify Neon's automated backup coverage and PITR window
  - [ ] Add nightly backup verification job to CI (test restore to temp DB)
  - [ ] Document RTO/RPO targets
  - [ ] Create disaster recovery runbook in `docs/DR-RUNBOOK.md`
  - [ ] Add Fly volume snapshot policy for non-Neon databases (Vault)
- **Dependencies**: Neon Postgres config
- **Risk if skipped**: No recovery path for data loss; compliance exposure for payments/GDPR data

---

## Tier 1 — High Priority (within 3 months)

### B-006: Tax calculation service
- **Effort**: L
- **Status**: Not started
- **Why**: `Payment.Tax` property exists but is caller-supplied with no validation. No Avalara/TaxJar integration. No jurisdiction-based calculation.
- **What to do**:
  - [ ] Evaluate Avalara vs TaxJar vs custom
  - [ ] Create Tax.Domain, Tax.Application, Tax.Infrastructure, Tax.Api
  - [ ] Wire into CheckoutOrchestrator before `PaymentSessionRequestedEvent`
  - [ ] Handle tax exemptions, merchant nexus, cross-border VAT
- **Dependencies**: CheckoutOrchestrator, Payments, Pricing
- **Risk if skipped**: Legal/financial liability in multi-jurisdiction operation

### B-007: Centralized rate limiting BuildingBlock
- **Effort**: M
- **Status**: Fragmented (Identity, Privacy have it; others don't)
- **What to do**:
  - [ ] Create `AddPlatformRateLimiting(IConfiguration)` in `BuildingBlocks/Extensions/`
  - [ ] Read policies from `RateLimiting` config section (per-IP, per-user, per-endpoint)
  - [ ] Apply globally via `ServiceDefaults.AddServiceDefaults()`
  - [ ] Migrate Identity and Privacy to use the shared extension
- **Dependencies**: B-004 (BFF implementation informs the shared pattern)

### B-008: Secrets rotation automation
- **Effort**: M
- **Status**: Vault dynamic DB creds work; static secrets never rotate
- **Why**: Stripe API keys, FCM credentials, S3 access keys, SendGrid tokens are set once as Fly secrets and never rotated.
- **What to do**:
  - [ ] Implement automated rotation for Stripe keys (Stripe supports key rolling)
  - [ ] Store static secrets in Vault KV with TTL alerts
  - [ ] Add rotation reminder job to Scheduler service
  - [ ] Document rotation procedures in `docs/SECRETS-ROTATION.md`
- **Dependencies**: Vault (already integrated), Scheduler

### B-009: Admin / Backoffice portal
- **Effort**: L
- **Status**: API endpoints exist; no UI
- **Why**: `AdminController` exists in Payments and Identity. `AuditQueryController` and `AuditExportController` exist. But no unified admin frontend.
- **What to do**:
  - [ ] Choose framework (React Admin, Refine, or custom Astro)
  - [ ] Build views: Merchant management, Order management, Refund processing, User management, Audit log viewer, Feature flag toggles
  - [ ] Wire to existing admin API endpoints
  - [ ] Add RBAC (Admin role required)
- **Dependencies**: Identity (admin auth), Audit (query API), Payments (refund API)

### B-010: Fraud detection via RulesEngine
- **Effort**: L
- **Status**: RulesEngine service exists; no fraud rules seeded
- **Why**: RulesEngine has full CRUD + expression evaluator. No rules exist for payment risk, velocity checks, or geographic anomalies.
- **What to do**:
  - [ ] Seed fraud detection rules (velocity limits, card testing patterns, geo anomalies)
  - [ ] Wire `EvaluateRuleQuery` into CheckoutOrchestrator before payment
  - [ ] Create `FraudCheckRequestedEvent` / `FraudCheckCompletedEvent` contracts
  - [ ] Add risk score to Payment entity
  - [ ] Build admin UI for rule management (extends B-009)
- **Dependencies**: RulesEngine (built), CheckoutOrchestrator, Payments

### B-011: Alertmanager + disaster recovery runbook
- **Effort**: S
- **Status**: `saga-alerts.yml` has 4 rules; no routing config
- **What to do**:
  - [ ] Configure Alertmanager with Slack/PagerDuty receivers
  - [ ] Route saga alerts, payment failure alerts, health check alerts
  - [ ] Write `docs/DR-RUNBOOK.md` with RTO/RPO targets, step-by-step recovery
  - [ ] Document service dependency graph for restart ordering
- **Dependencies**: B-001 (Prometheus must be running first)

---

## Tier 2 — Medium Priority (3-12 months)

### B-012: Recommendation engine
- **Effort**: L
- **Why**: Analytics collects clickstream to Kafka but nothing consumes it for recommendations. No "customers also bought" or personalization.
- **Dependencies**: Analytics (clickstream), Search (serving), Catalog

### B-013: Canary / blue-green deployments
- **Effort**: M
- **Why**: Every deploy is all-or-nothing. `deploy.yml` uses `flyctl deploy` directly with no canary weight splitting or smoke gate.
- **Dependencies**: Observability (B-001 — need metrics to detect canary failures)

### B-014: Customer support / ticketing integration
- **Effort**: M
- **Why**: No Zendesk/Freshdesk/Intercom integration. Customer issues resolved manually.
- **Dependencies**: Identity (user lookup), Orders (order context)

### B-015: A/B testing framework
- **Effort**: M
- **Why**: FeatureFlags has percentage rollout. Analytics has clickstream. But no experiment tracking or statistical significance reporting.
- **Dependencies**: FeatureFlags (cohort), Analytics (metrics)

### B-016: Multi-tenancy BuildingBlock
- **Effort**: L
- **Why**: No `ITenantContext`, no row-level security, no schema-per-tenant. Merchant scoping is FK-based only.
- **Dependencies**: All services (cross-cutting concern)

### B-017: Shipping / fulfillment integration
- **Effort**: L
- **Why**: Address captured at checkout but no carrier integration (UPS/FedEx/EasyPost), no label generation, no tracking propagation.
- **Dependencies**: Orders (shipment status), CheckoutOrchestrator

### B-018: Web Push / APNs notification channel
- **Effort**: M
- **Why**: `NotificationChannel.Push` enum exists. FCM config mentioned but not deployed. No VAPID web push.
- **Dependencies**: Notifications service, Identity (device registration)

---

## Tier 3 — Low Priority (future)

### B-019: Event sourcing / CQRS read projections
- **Effort**: L | Current architecture (EF write-through + outbox) is appropriate for current scale.

### B-020: Reporting / BI layer
- **Effort**: L | No data warehouse, dbt models, or self-serve reporting.

### B-021: Social features (follows, feeds, wishlists)
- **Effort**: L | Not core to marketplace. Relevant if platform pursues community model.

---

## BuildingBlocks Gaps

| ID | Gap | Priority | Effort | Status |
|----|-----|----------|--------|--------|
| BB-01 | `AddPlatformRateLimiting()` shared extension | High | S | See B-007 |
| BB-02 | Circuit breaker Grafana dashboard | Medium | S | Polly emits metrics; needs dashboard |
| BB-03 | Standardize migration orchestration | Medium | S | Each service has own `MigrateWithRetryAsync` pattern |
| BB-04 | Cache invalidation event contract | Medium | S | HybridCache present; no standard cross-service bust |
| BB-05 | Distributed lock abstraction | Low | M | Redis-based; needed for multi-instance coordination |

---

## Completed Items

- [x] ~~Timeout consolidation (`HttpClientTimeoutOptions`)~~ — PR #121, merged 2026-05-16
- [x] ~~Architecture guard generalization~~ — PR #121, merged 2026-05-16
- [x] ~~Audit transient Postgres resilience~~ — PR #123, 2026-05-16
- [x] ~~Portfolio site demo buttons working~~ — Cloudflare Pages deploy, 2026-05-16
- [x] ~~Service-to-service JWT auth~~ — Identity service token + BFF forwarding
- [x] ~~JWKS validation fix (.NET 9 PostConfigure bug)~~ — Direct config in AddJwtBearer delegate
- [x] ~~104 security findings across 3 waves~~ — All resolved, 63+ arch guards
