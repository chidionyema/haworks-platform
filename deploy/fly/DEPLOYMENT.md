# Fly.io Deployment Guide

## Architecture

The platform runs as microservices on Fly.io, with each service as a separate Fly app communicating over Fly's private 6PN network (`.internal` DNS).

```
Internet
  │
  ▼
haworks-bffweb (public, HTTPS)
  │ .internal:8080
  ├── haworks-identity     (auth, JWT, Vault integration)
  ├── haworks-catalog      (products, cache, inventory)
  ├── haworks-orders       (order management, idempotency)
  ├── haworks-payments     (Stripe, outbox, refunds)
  ├── haworks-checkout     (saga orchestrator)
  ├── haworks-search       (Elasticsearch, CDC)
  ├── haworks-payouts      (double-entry ledger, Hangfire)
  └── haworks-privacy      (GDPR erasure)
```

## Infrastructure

| Component | Provider | Notes |
|-----------|----------|-------|
| PostgreSQL | Supabase (free tier) | Single DB, per-service schemas via EF migrations |
| RabbitMQ | CloudAMQP | MassTransit transport + outbox relay |
| Redis | Upstash | HybridCache L2, rate limiting |
| Elasticsearch | Fly (self-hosted) | CDC search index |
| Vault | Fly (self-hosted) | Secrets rotation demo |

## Prerequisites

- `flyctl` CLI authenticated
- Supabase project with database password
- CloudAMQP instance (amqps:// URI)
- Upstash Redis instance (rediss:// URI)

## Setup (First Time)

1. Copy the env template:
   ```bash
   cp deploy/fly/.env.example deploy/fly/.env.local
   ```

2. Fill in `.env.local`:
   ```env
   REGION=iad
   RABBITMQ_URL=amqps://...
   REDIS_URL=rediss://...
   POSTGRES_BASE=postgres://postgres:PASSWORD@db.PROJECT.supabase.co
   POSTGRES_QUERY=?sslmode=require
   ```

3. Run bootstrap (creates apps, stages secrets, generates JWT keypair):
   ```bash
   deploy/fly/bootstrap.sh
   ```

4. Deploy all services:
   ```bash
   deploy/fly/deploy.sh
   ```

## Deploy a Single Service

```bash
flyctl deploy -c fly.catalog.toml --remote-only --ha=false
```

## Service → fly.toml Mapping

Each service has a `fly.{name}.toml` in the repo root. The deploy script uses:
```bash
flyctl deploy -c fly.{service}.toml --remote-only --ha=false
```

## Connection String Convention

Each service reads `ConnectionStrings:{service-name}` from config.
Bootstrap composes ADO.NET-form strings from `POSTGRES_BASE`:
```
Host={host};Port=5432;Database={service};Username={user};Password={pass};SslMode=Require;Trust Server Certificate=true
```

For Supabase (single DB), all services share the `postgres` database but use separate EF migration schemas.

## Budget-Conscious Settings

For minimal cost (~$30/mo):
- `shared-cpu-1x` with 256MB RAM per service
- `auto_stop_machines = "stop"` on non-critical services (payouts, privacy)
- `min_machines_running = 0` on non-critical services
- Only BFF has `min_machines_running = 1` (always-on)
- Catalog keeps `min_machines_running = 1` (5 demos depend on it)

## Demo Endpoint → Service Map

| Demo | Primary Service | Secondary |
|------|----------------|-----------|
| Checkout Saga | checkout-svc | catalog-svc |
| Transactional Outbox | payments-svc | — |
| Circuit Breaker | catalog-svc | — |
| Optimistic Concurrency | catalog-svc | — |
| Cache Stampede | catalog-svc | — |
| Cache Invalidation | catalog-svc | — |
| Rate Limiting | bffweb (in-process) | — |
| Idempotency | orders-svc | — |
| Refund Saga | payments-svc | — |
| Double-Entry Ledger | payouts-svc | — |
| GDPR Erasure | privacy-svc | — |
| CDC Search | search-svc | elasticsearch |
| Vault Rotation | identity-svc | vault |

## Secrets Management

Secrets are staged (not deployed) by `bootstrap.sh`. They take effect on the next `flyctl deploy`. To update a single secret:

```bash
flyctl secrets set -a haworks-catalog "ConnectionStrings__catalog=Host=..." --stage
flyctl deploy -c fly.catalog.toml --remote-only --ha=false
```

Common secrets shared across all services:
- `ConnectionStrings__rabbitmq`
- `ConnectionStrings__redis`
- `Authentication__Jwks__*` (JWT validation)
- `Vault__*` (HashiCorp Vault)
- `MT_LICENSE` (MassTransit)
