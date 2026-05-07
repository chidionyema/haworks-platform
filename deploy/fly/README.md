# Deploying to Fly.io

End-to-end deploy for the seven ritualworks microservices on Fly.io. The
local Aspire AppHost (`deploy/aspire/`) is unaffected by anything here —
it keeps working for local dev. This directory only covers Fly.

## What runs where

| Service | Fly app | Public? | Reachable at |
|---|---|---|---|
| BFF | `ritualworks-bffweb` | yes | `https://ritualworks-bffweb.fly.dev` |
| Identity | `ritualworks-identity` | no | `http://ritualworks-identity.flycast:8080` |
| Catalog | `ritualworks-catalog` | no | `http://ritualworks-catalog.flycast:8080` |
| Orders | `ritualworks-orders` | no | `http://ritualworks-orders.flycast:8080` |
| Payments | `ritualworks-payments` | no | `http://ritualworks-payments.flycast:8080` |
| Checkout | `ritualworks-checkout` | no | `http://ritualworks-checkout.flycast:8080` |
| Content | `ritualworks-content` | no, opt-in | `http://ritualworks-content.flycast:8080` |

Backends are private — only the BFF gets a public IP. Inter-service traffic
goes over Fly's flycast (private 6PN with load balancing).

## Prerequisites

1. **Install flyctl + openssl + dotnet 9 SDK.** macOS: `brew install flyctl openssl`.
2. **Fly account, logged in:** `flyctl auth login`.
3. **Three managed services** (free tiers are fine for a portfolio deploy):

| Provider | What you grab |
|---|---|
| **Neon** (Postgres) | One project; create 6 databases inside it (`identity`, `catalog`, `orders`, `payments`, `checkout`, `content`). Copy the connection string from the dashboard — only the database-name segment differs per service. |
| **CloudAMQP** (RabbitMQ) | One instance; copy the AMQPS URL. |
| **Upstash** (Redis) | One database; copy the `rediss://` URL. |

Pick regions close to Fly `iad` (US East) — Neon `us-east-1`, Upstash
`us-east-1`, CloudAMQP region of choice. CloudAMQP in `eu-west-1` adds
~70ms per AMQP round-trip from `iad`.

## First deploy — one command

```bash
cd ritualworks-platform
deploy/fly/up.sh
```

What it does, in order:

1. **Prereq check** — flyctl installed, logged in, openssl available.
2. **Env file** — copies `.env.example` → `.env.local` if missing, asks you
   to fill in `RABBITMQ_URL`, `REDIS_URL`, `POSTGRES_BASE`. Press Enter to
   continue. `.env.local` is gitignored (the `.env.*` rule already covers it).
3. **Bootstrap** — generates an RSA-2048 JWT signing key on first run and
   persists it back to `.env.local` so future redeploys keep the same key
   (existing tokens stay valid). Creates the seven Fly apps if missing,
   allocates a public IPv4+IPv6 only on the BFF, stages all per-service
   secrets via `flyctl secrets import --stage`.
4. **Deploy** — identity first (others auth against it), then
   catalog/orders/payments/checkout in parallel, then BFF.
5. **Status summary** — prints per-app status and the public URL.

Re-run safely after editing `.env.local`. It's fully idempotent.

## What's in `.env.local`

Three required values, the rest optional. See `.env.example` for the full
template with comments.

```
RABBITMQ_URL=amqps://USER:PASS@host.cloudamqp.com/VHOST
REDIS_URL=rediss://default:TOKEN@host.upstash.io:6379
POSTGRES_BASE=postgres://default:PASS@ep-xxx-pooler.us-east-1.aws.neon.tech
POSTGRES_QUERY=?sslmode=require&channel_binding=require

JWT_SIGNING_KEY_PEM=        # auto-generated on first bootstrap; do not edit
JWT_KEY_ID=fly-1            # bumps if you rotate keys

# Optional — leave blank if unused. Identity boots without OAuth providers.
OAUTH_GOOGLE_CLIENT_ID=
OAUTH_GOOGLE_CLIENT_SECRET=
# (microsoft, facebook, stripe webhook also optional)

DEPLOY_CONTENT=false        # see "Adding Content" below
```

**Never commit `.env.local`.** It contains every credential the platform
needs. The repo's `.gitignore` already excludes `.env.*` but check before
pushing.

## Subsequent deploys

```bash
deploy/fly/deploy.sh        # re-deploys all services
flyctl deploy -c fly.<svc>.toml --remote-only   # one service
```

Or via GitHub Actions: `git push origin main` triggers
`.github/workflows/deploy.yml` after CI passes. The workflow needs a single
repo secret:

```
FLY_API_TOKEN = <output of: flyctl tokens create deploy --name github-actions>
```

Set it at *Repo → Settings → Secrets and variables → Actions → New repository secret*.

## Rotating a secret

Edit `.env.local`, then re-run `deploy/fly/bootstrap.sh` to restage, then
`deploy/fly/deploy.sh` to apply. Or for a one-off:

```bash
flyctl secrets set -a ritualworks-payments \
  Webhooks__Stripe__WebhookSecret='whsec_...'
```

## Rollback

Per-service:

```bash
flyctl releases list -a ritualworks-<svc>
flyctl releases rollback -a ritualworks-<svc>
```

Cross-service rollback isn't automated — roll back each app individually.

## Adding Content service

Default skips `ritualworks-content` because it needs S3-compatible storage.
To opt in with Fly Tigris:

```bash
# 1. Create the app first so storage attaches to it.
flyctl apps create ritualworks-content

# 2. Provision Tigris. flyctl prints the credentials to stdout.
flyctl storage create -a ritualworks-content

# 3. Copy the printed AWS_* values into .env.local's MINIO_* slots:
#    AWS_ENDPOINT_URL_S3       → MINIO_ENDPOINT (without https://)
#    AWS_ACCESS_KEY_ID         → MINIO_ACCESS_KEY
#    AWS_SECRET_ACCESS_KEY     → MINIO_SECRET_KEY
#    BUCKET_NAME               → MINIO_BUCKET
#    MINIO_SECURE=true
# 4. Set DEPLOY_CONTENT=true in .env.local.

deploy/fly/up.sh             # re-runs end to end with content
```

ClamAV (`CLAMAV_REST_URL`) is optional. Without it, uploads succeed without
virus scanning — fine for a portfolio demo, not for real user uploads.

## Stripe webhook

After payments-svc is up, register the webhook in the Stripe dashboard:

```
URL:    https://ritualworks-bffweb.fly.dev/api/payments/webhook
Events: payment_intent.succeeded, payment_intent.payment_failed
        checkout.session.completed
```

Copy the signing secret Stripe gives you and either re-run `bootstrap.sh`
with `STRIPE_WEBHOOK_SECRET=whsec_...` in `.env.local`, or one-shot:

```bash
flyctl secrets set -a ritualworks-payments \
  Webhooks__Stripe__WebhookSecret='whsec_...'
```

## Troubleshooting

**Identity crashloops with "Jwt:SigningKeyPem is required when Vault:Enabled=false".**
The bootstrap didn't generate the key, or the env file was edited and the
key field cleared. Re-run `deploy/fly/bootstrap.sh` — it auto-generates if
the field is empty.

**A backend service crashloops with "ConnectionStrings:rabbitmq is missing".**
Bootstrap failed for that app. Run `flyctl secrets list -a ritualworks-<svc>`
to confirm. Re-run `bootstrap.sh`.

**Identity boots but `/api/external-authentication/google-callback` returns 404.**
You didn't supply `OAUTH_GOOGLE_CLIENT_ID`. Conditional registration means
the route is only mapped when credentials are present. Add them and redeploy.

**BFF returns 502 from `/hubs/checkout` or other backend calls.**
Backend service is asleep (auto-stop), or the `Services__<svc>__http__0`
override doesn't match a real flycast hostname. Verify with:
```bash
flyctl status -a ritualworks-<svc>
flyctl secrets list -a ritualworks-bffweb | grep Services__
```

**EF migration fails on first deploy.** Neon's `default` role has owner
privileges, so DDL should succeed. If it fails, check the migration
dependency order — services migrate in parallel and only their own DBs.
Manual fix: `flyctl ssh console -a ritualworks-<svc>` then run the
migration command directly.

**`flyctl deploy` build errors on missing `Directory.Build.props`.** The
Dockerfile build context must be the repo root. The `fly.<svc>.toml` files
already point at `src/<Service>/<Service>.Api/Dockerfile` with build
context = repo root. If you copy a Dockerfile out, keep the path relative
to the repo root.

## Local dev still uses Aspire

`dotnet run --project deploy/aspire` is unchanged. The same code paths that
work on Fly (Vault disabled, `Jwt:SigningKeyPem` from config) are exercised
in dev when `Vault:Enabled=true` flips to false — but the AppHost still
defaults to Vault-enabled, so local dev keeps working as before. Nothing
to change.

## Files in this directory

```
.env.example       — template for .env.local
.env.local         — your filled-in secrets (gitignored, never committed)
bootstrap.sh       — creates apps + stages secrets; idempotent
deploy.sh          — deploys all services in dependency order
up.sh              — one-command entrypoint (bootstrap + deploy + summary)
README.md          — this file
```
