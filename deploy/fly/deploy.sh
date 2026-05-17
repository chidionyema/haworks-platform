#!/usr/bin/env bash
# Deploy services in dependency order:
#   1. identity                                    (others may auth at boot)
#   2. all backend services                        (parallel)
#   3. bffweb                                      (talks to all backends)
#
# DEPLOY_MEDIA=true (in .env.local) opts into media-svc (S3/ClamAV); default skips it.
# Run bootstrap.sh first (or any time .env.local changes) to stage secrets.

set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT_DIR"

ENV_FILE="$ROOT_DIR/deploy/fly/.env.local"
DEPLOY_MEDIA="false"
if [[ -f "$ENV_FILE" ]]; then
  # shellcheck disable=SC1090
  set -a; source "$ENV_FILE"; set +a
fi
DEPLOY_MEDIA="${DEPLOY_MEDIA:-false}"

# ── Step 0: Fresh Vault credentials ──────────────────────────────────
echo ">>> staging fresh Vault credentials (wrapped, 30-min TTL)"
if [[ -x "$ROOT_DIR/deploy/fly/ci-stage-vault-creds.sh" ]]; then
  "$ROOT_DIR/deploy/fly/ci-stage-vault-creds.sh" || {
    echo "WARN: Vault credential staging failed — services with Vault enabled may crash on boot" >&2
    echo "      This is expected if Vault is not deployed yet. Continuing..." >&2
  }
else
  echo "WARN: ci-stage-vault-creds.sh not found or not executable — skipping Vault credential staging"
fi
echo ""

deploy_one() {
  local svc="$1"
  echo ">>> deploying $svc"
  flyctl deploy -c "fly.${svc}.toml" --remote-only --ha=false
}

deploy_one identity

PARALLEL=(catalog orders payments checkout audit search webhooks notifications scheduler payouts pricing merchant privacy location realtime featureflags analytics localization rulesengine)
[[ "$DEPLOY_MEDIA" == "true" ]] && PARALLEL+=(media)

pids=()
for svc in "${PARALLEL[@]}"; do
  ( deploy_one "$svc" ) &
  pids+=($!)
done
fail=0
for pid in "${pids[@]}"; do
  wait "$pid" || fail=1
done
[[ $fail -eq 0 ]] || { echo "Backend deploy failed; aborting BFF" >&2; exit 1; }

deploy_one bffweb
echo "All services deployed."
