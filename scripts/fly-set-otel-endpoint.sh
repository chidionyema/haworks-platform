#!/usr/bin/env bash
# =============================================================================
# Sets OTEL_EXPORTER_OTLP_ENDPOINT secret on every .NET app in the platform.
#
# Why this exists: the Fly toml [env] blocks already carry the static OTel
# identity attributes (service.name, deployment.environment, service.namespace)
# but Fly env values do NOT interpolate ${VAR}, so a per-environment URL has
# to live as a secret and be applied app-by-app. This loops the apps so the
# operator does not have to remember the exact list or the syntax.
#
# Usage:
#   ./scripts/fly-set-otel-endpoint.sh <otlp-endpoint>
#
# Examples:
#   ./scripts/fly-set-otel-endpoint.sh https://tempo-prod-04-prod-us-east-0.grafana.net:443
#   ./scripts/fly-set-otel-endpoint.sh https://api.honeycomb.io:443
#   ./scripts/fly-set-otel-endpoint.sh http://otel-gw.internal:4317
#
# Skips infra apps (haworks-vault, haworks-vault-pg,
# haworks-meilisearch) — they have no .NET runtime and no OTel SDK loaded.
#
# Each `fly secrets set` call triggers a rolling redeploy of that app's
# machines. Expect ~30s per app while machines roll. Run from a host with
# `flyctl auth login` already done.
#
# See docs/runbooks/observability-fly-otlp-secret.md for the full operator
# story (rotation, verification, what-if-I-forget, choosing a backend).
# =============================================================================
set -euo pipefail

# ---- arg parsing -----------------------------------------------------------

usage() {
    cat <<EOF
Usage: $(basename "$0") <otlp-endpoint> [otlp-headers]

Sets OTEL_EXPORTER_OTLP_ENDPOINT (and optionally OTEL_EXPORTER_OTLP_HEADERS)
on every .NET Fly app in the platform.

The endpoint must start with http:// or https:// and point at an OTLP
collector (Tempo, Honeycomb, Grafana Cloud, self-hosted otel-collector,
etc). Both HTTP/protobuf (port 4318) and gRPC (port 4317) are supported by
the SDK; the URL scheme is what matters here.

The optional headers argument sets OTEL_EXPORTER_OTLP_HEADERS for backends
that require authentication (Grafana Cloud, Honeycomb, etc).

Examples:
  # Self-hosted collector (no auth)
  $(basename "$0") http://otel-gw.internal:4317

  # Grafana Cloud OTLP gateway
  $(basename "$0") https://otlp-gateway-prod-us-east-0.grafana.net/otlp \\
      "Authorization=Basic <base64(instanceId:cloudApiKey)>"

  # Honeycomb
  $(basename "$0") https://api.honeycomb.io:443 \\
      "x-honeycomb-team=<api-key>"
EOF
}

if [[ $# -lt 1 || $# -gt 2 ]]; then
    usage >&2
    exit 1
fi

URL="$1"
HEADERS="${2:-}"

if [[ ! "$URL" =~ ^https?:// ]]; then
    echo "ERROR: endpoint must start with http:// or https:// (got: $URL)" >&2
    usage >&2
    exit 1
fi

# ---- app list --------------------------------------------------------------
#
# Logical service names map to Fly app names with the `haworks-` prefix
# (cf. fly.*.toml `app =` line). Keep this list in sync with the .NET apps
# that have OTel auto-instrumentation wired in BuildingBlocks.Observability.
#
# Skipped on purpose:
#   haworks-vault, haworks-vault-pg  — Hashicorp Vault + Postgres,
#                                              no .NET, no OTel SDK.
#   haworks-meilisearch                  — vendor image, emits its own
#                                              telemetry on a different path.
APPS=(
    haworks-bffweb
    haworks-catalog
    haworks-checkout
    haworks-content
    haworks-identity
    haworks-location
    haworks-media
    haworks-merchant
    haworks-notifications
    haworks-orders
    haworks-payments
    haworks-payouts
    haworks-privacy
    haworks-scheduler
    haworks-search
    haworks-audit
    haworks-webhooks
)

# ---- main loop -------------------------------------------------------------

if ! command -v flyctl >/dev/null 2>&1; then
    echo "ERROR: flyctl not on PATH. Install from https://fly.io/docs/hands-on/install-flyctl/" >&2
    exit 1
fi

# Track per-app outcomes for the summary table at the end. We deliberately do
# NOT abort the loop on a single failure — a missing app (e.g. audit-svc not
# yet provisioned) should not block applying the secret to the other 9.
declare -a RESULTS=()

# Disable -e inside the loop so a single failed app does not kill the script.
# We re-check the rc explicitly per call.
set +e
for APP in "${APPS[@]}"; do
    echo
    echo "===> $APP"
    SECRETS="OTEL_EXPORTER_OTLP_ENDPOINT=$URL"
    if [[ -n "$HEADERS" ]]; then
        SECRETS="$SECRETS OTEL_EXPORTER_OTLP_HEADERS=$HEADERS"
    fi
    # shellcheck disable=SC2086 — intentional word splitting on $SECRETS
    if flyctl secrets set $SECRETS -a "$APP"; then
        RESULTS+=("OK    $APP")
    else
        RESULTS+=("FAIL  $APP")
    fi
done
set -e

# ---- summary ---------------------------------------------------------------

echo
echo "============================================================"
echo "OTEL_EXPORTER_OTLP_ENDPOINT applied to:"
echo "  $URL"
echo "------------------------------------------------------------"
printf '  %s\n' "${RESULTS[@]}"
echo "============================================================"

# Exit non-zero if anything failed, so CI / chained scripts notice.
for R in "${RESULTS[@]}"; do
    case "$R" in
        FAIL*) exit 2 ;;
    esac
done
