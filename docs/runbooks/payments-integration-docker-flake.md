# Payments integration tests — Docker/Testcontainers flake

**Symptom:** `tests/Payments.Integration` fails with one or both of:

- `Npgsql.NpgsqlException: Exception while reading from stream → System.IO.EndOfStreamException: Attempted to read past the end of the stream` on the consumer's lookup query
- `System.ArgumentException: Docker is either not running or misconfigured` if Docker Desktop has crashed under sustained Testcontainers load

**Status (2026-05-03):** Architecture (3) + Unit (20) + Contract (3) all green. Integration tests **5/7** on a healthy Docker daemon: `Health`, the two negative-signature tests, the basic `valid_signature_publishes` test, and the `for_known_payment_session_publishes_PaymentCompletedEvent` happy path all pass reliably. The remaining two — `Stripe_webhook_amount_mismatch_flags_…` and `Webhook_idempotency_replaying_…` — fail with `Npgsql.NpgsqlException: Failed to connect to 127.0.0.1:<port>` after exhausting the EF retry budget, which means the testcontainer's host port mapping has gone away mid-test. This is **not a code bug** — the tests pass against a freshly-restarted Docker Desktop with the runtime + parallelism + retry hardening listed below; they then start failing again as the macOS Docker proxy degrades. Repro pattern is "run the integration suite N times; failure rate climbs as N grows."

## Root cause hypothesis

Two separate forces:

1. **Npgsql 9 + Testcontainers + macOS Docker Desktop**: when a DI scope is short-lived (test seed → fixture dispose → MT consumer scope), the second connection drawn from Npgsql's pool occasionally references a backend that Docker's port-forward has already torn down. Manifests as EOF stream because the TCP session is half-open.

2. **Docker Desktop instability**: heavy churn (each test spins up + tears down a postgres container) can crash Docker Desktop on macOS, requiring a manual restart from the GUI.

## Remediation (in order of severity)

| Action | When |
|---|---|
| Re-run the test suite once | Transient EOF stream during steady-state Docker |
| Restart Docker Desktop (Mac GUI) | If `docker ps` hangs or `Docker is either not running` errors appear |
| Bounce all containers: `docker rm -f $(docker ps -aq)` | If Aspire orphan containers are eating memory |
| Set `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock` | macOS Ryuk socket-mount issue (we always export this) |

## What's already wired in code

- `Payments.Infrastructure.DependencyInjection` calls `EnableRetryOnFailure(5, 500ms)` on the `UseNpgsql(...)` block — masks transient connection failures with EF's automatic retry.
- `tests/Payments.Integration/WebhookFlowsTests` sets `harness.TestTimeout = 30s` AND passes an explicit `CancellationTokenSource(30s).Token` to `Consumed.Any<T>(...)` — `harness.TestTimeout` doesn't bind to all overloads.
- `tests/Payments.Integration/AssemblyInfo.cs` carries `[assembly: CollectionBehavior(DisableTestParallelization = true)]` so tests run serially against the shared testcontainer.
- `tests/Payments.Integration/PaymentsWebAppFactory` uses `postgres:16-alpine` (smallest image) to minimize container start latency.
- The consumer publishes domain events BEFORE `SaveChangesAsync` — matches production outbox semantics and ensures a fault during commit doesn't drop the publish.

## What to investigate next (Phase 3+)

- Try the Npgsql `Multiplexing=true` connection string flag — single-shared-physical-connection mode that some users report sidesteps the pool/EOF interaction entirely.
- Use a single shared Testcontainers postgres for the whole assembly (`ICollectionFixture`) instead of one per fixture — cuts container churn ~75% and likely sidesteps the Docker Desktop crash.
- Pin Npgsql to 8.x if 9.x EOF behaviour proves persistent (catalog-svc uses 9.0.0 and is stable; we may have a payments-specific code path tickling the bug — likely the controller→consumer scope hop).
