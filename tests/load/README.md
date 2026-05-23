# Load Tests

k6 load tests for validating SLOs under realistic and stress conditions.

## Prerequisites

```bash
# macOS
brew install k6

# Linux (Debian/Ubuntu)
sudo gpg -k
sudo gpg --no-default-keyring --keyring /usr/share/keyrings/k6-archive-keyring.gpg \
  --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys C5AD17C747E3415A3642D57D77C6C491D6AC1D68
echo "deb [signed-by=/usr/share/keyrings/k6-archive-keyring.gpg] https://dl.k6.io/deb stable main" \
  | sudo tee /etc/apt/sources.list.d/k6.list
sudo apt-get update && sudo apt-get install k6

# Docker
docker pull grafana/k6

# Windows
choco install k6
# or: winget install k6
```

Verify installation:

```bash
k6 version
```

## Tests

| Script | What it tests | Key SLOs |
|--------|--------------|----------|
| `checkout-flow.js` | Full checkout saga (browse, reserve, confirm, checkout) | 99.9% success, P99 < 30s |
| `media-upload.js` | Presigned URL generation throughput | 99% success, P99 < 2s |
| `browse-and-search.js` | Product browse + full-text + AI semantic search | P95 < 200ms browse, P95 < 500ms search |
| `payment-webhooks.js` | Stripe webhook delivery + idempotency verification | P95 < 1s, 0 errors, 100% idempotency |
| `refund-flow.js` | Refund request creation + polling to terminal state | P95 < 5s, 99% success |
| `notifications.js` | Notification creation + delivery polling | P95 < 10s, 99% success |
| `ai-chat.js` | Streaming AI chat with 3-turn conversations | TTFT P95 < 2s, total P95 < 10s |
| `merchant-onboarding.js` | Merchant creation + slug uniqueness under concurrency | P95 < 500ms, 0 duplicate slugs |
| `ledger-accounting.js` | Concurrent credit/debit operations + balance consistency | P95 < 500ms, 0 balance drift |
| `gdpr-erasure.js` | Privacy erasure request + cross-service fan-out polling | P95 < 30s, 95% success |
| `audit-trail.js` | Audit log queries (recent, filtered, wide range) | P95 < 1s |
| `soak-test.js` | All workloads at 5 req/s for 1 hour (leak detection) | 99% success, latency drift < 2x |

### Shared Helpers

| File | Purpose |
|------|---------|
| `helpers/auth.js` | Auth token resolution (ENV, service-token, login fallback) |
| `helpers/config.js` | BASE_URL, SLO constants, stages, utility functions |

## Environment Variables

| Variable | Default | Used By |
|----------|---------|---------|
| `BASE_URL` | `http://localhost:5000` | All scripts |
| `AUTH_TOKEN` | _(none)_ | All scripts (skips login if set) |
| `SERVICE_SECRET` | `load-test-secret` | Auth helper fallback |
| `STRIPE_WEBHOOK_SECRET` | `whsec_test_load_secret` | `payment-webhooks.js` |
| `PRODUCT_ID` | `00000000-...0001` | `checkout-flow.js`, `soak-test.js` |
| `ORDER_ID` | _(random UUID)_ | `refund-flow.js` |
| `SELLER_ID` | _(per-VU deterministic)_ | `ledger-accounting.js` |
| `TARGET_USER_ID` | _(random UUID)_ | `notifications.js` |
| `SUBJECT_USER_ID` | _(random UUID)_ | `gdpr-erasure.js` |

## Running Individual Tests

### Against local (docker-compose)

```bash
# Start the platform
cd deploy/compose && docker compose up -d

# Run any individual test
k6 run tests/load/checkout-flow.js
k6 run tests/load/browse-and-search.js
k6 run tests/load/payment-webhooks.js
k6 run tests/load/refund-flow.js
k6 run tests/load/notifications.js
k6 run tests/load/ai-chat.js
k6 run tests/load/merchant-onboarding.js
k6 run tests/load/ledger-accounting.js
k6 run tests/load/gdpr-erasure.js
k6 run tests/load/audit-trail.js

# Media upload (constant arrival rate, not ramping-vus)
k6 run tests/load/media-upload.js
```

### Against Fly (production)

```bash
k6 run -e BASE_URL=https://haworks-bffweb.fly.dev \
       -e AUTH_TOKEN=your-jwt-token \
       tests/load/browse-and-search.js
```

### With a pre-provisioned auth token

```bash
# Get a token first
TOKEN=$(curl -s https://haworks-bffweb.fly.dev/api/v1/authentication/service-token \
  -H "X-Service-Secret: $SECRET" | jq -r .accessToken)

# Pass to any test
k6 run -e BASE_URL=https://haworks-bffweb.fly.dev \
       -e AUTH_TOKEN="$TOKEN" \
       tests/load/ledger-accounting.js
```

### Webhook test with signing secret

```bash
k6 run -e STRIPE_WEBHOOK_SECRET=whsec_your_secret \
       tests/load/payment-webhooks.js
```

## Running the Soak Test

The soak test runs ALL workloads simultaneously at 5 req/s each for 1 hour.
It detects memory leaks, connection pool exhaustion, and queue depth growth.

```bash
# Full 1-hour soak (requires significant resources)
k6 run tests/load/soak-test.js

# Against production with auth
k6 run -e BASE_URL=https://haworks-bffweb.fly.dev \
       -e AUTH_TOKEN="$TOKEN" \
       tests/load/soak-test.js
```

**What the soak test catches:**
- Response time drift (P95 at minute 5 vs minute 55)
- Connection pool exhaustion (growing latency over time)
- Memory leaks (OOM kills during the run)
- Queue depth growth (messages backing up)

**Interpreting soak results:**
- Compare `soak_p95_early` vs `soak_p95_late` -- if late is >2x early, there is a leak
- Watch per-workload metrics (`soak_browse_latency`, `soak_ledger_latency`, etc.)
- Any `soak_total_errors` > 1% of `soak_total_requests` indicates degradation

### With Grafana Cloud k6

```bash
K6_CLOUD_TOKEN=your-token k6 cloud tests/load/soak-test.js
```

## Interpreting Results

k6 exits with code 99 if any threshold is breached. Key things to look for:

### Per-test SLO checks

| Metric pattern | Meaning |
|----------------|---------|
| `*_success_rate` | Percentage of operations that completed successfully |
| `*_latency p(95)` | 95th percentile latency for the operation |
| `http_req_failed` | Raw HTTP error rate (5xx, timeouts) |
| `*_balance_drift` | Ledger: non-zero means money was lost or created |
| `*_duplicate_slug_errors` | Merchant: concurrency bug in slug generation |
| `*_idempotency_rate` | Webhooks: <100% means duplicate processing occurred |

### Common failure patterns

- **P95 breach but low error rate**: Backend is slow but healthy. Check DB queries, N+1, missing indexes.
- **High error rate + fast responses**: Auth failures, 404s, or upstream rejecting requests. Check tokens.
- **Latency drift in soak**: Memory leak or connection pool exhaustion. Check GC pressure and Npgsql pool stats.
- **Balance drift > 0**: Critical concurrency bug. Check pessimistic locking and transaction boundaries.
- **Idempotency < 100%**: Webhook deduplication is broken. Check inbox/idempotency journal.

## SLO Definitions

| SLO | Target | Rationale |
|-----|--------|-----------|
| Browse latency P95 | < 200ms | Catalog reads are cached, must be fast |
| Search latency P95 | < 500ms | Elasticsearch/full-text queries |
| Checkout E2E P99 | < 30s | Saga involves reservation + payment + order |
| Webhook processing P95 | < 1s | Stripe expects fast acknowledgment |
| Refund completion P95 | < 5s | Includes async saga state transitions |
| Notification delivery P95 | < 10s | Email/push delivery can be async |
| AI TTFT P95 | < 2s | User-perceived responsiveness for streaming |
| AI total response P95 | < 10s | Full LLM response generation |
| Merchant onboarding P95 | < 500ms | Simple CRUD with slug check |
| Ledger operations P95 | < 500ms | Financial ops must be fast and consistent |
| GDPR erasure P95 | < 30s | Cross-service fan-out (6+ services) |
| Audit queries P95 | < 1s | Indexed time-range queries |
| Overall error rate | < 1% | Platform-wide HTTP error budget |
| Balance drift | 0 | Zero tolerance for financial inconsistency |

## Tuning Connection Pooling

After load testing, tune Npgsql pool sizes based on observed connection counts:

```bash
# Check active connections per service in Prometheus:
# sum(pg_stat_activity_count) by (datname)

# If connections hit MaxPoolSize (default 100 in Npgsql):
# Add to connection string: "Maximum Pool Size=200;Minimum Pool Size=10"
```
