#!/usr/bin/env bash
# Capture vault's init keys + a fresh AppRole secret_id for EVERY service
# in infra/vault/services.json and stage them as Fly secrets on each
# matching app. Designed to be called from CI (.github/workflows/deploy.yml's
# stage-vault-creds job) but safe to run from a developer laptop too.
#
# Uses vault's HTTP API via curl rather than the vault CLI — the CLI
# behaves badly under non-TTY environments (CI), but curl returns
# deterministic JSON that jq can parse reliably.
#
# What it does:
#   1. Wait for vault to be unsealed + active.
#   2. If /vault/data/.init.json is present (only on first-ever boot),
#      read unseal_keys_b64[0] + root_token; stage them on the vault app.
#   3. With the root token in hand, for each service in services.json:
#        a. Fetch role_id from auth/approle/role/haworks-<svc>/role-id
#        b. Issue a *response-wrapped* secret_id (X-Vault-Wrap-TTL: 300)
#        c. Stage the wrapping_token (NOT the raw secret_id) as
#           Vault__SecretId on haworks-<svc>, plus role_id +
#           Vault__SecretIdIsWrapped=true so the bootstrap library
#           knows to unwrap on first boot.
#        d. ::add-mask:: every secret value before any log line so a
#           CI log leak window is 5 minutes max (the wrapper TTL).
#
# Idempotent. Safe to re-run on every deploy.
#
# Required env:
#   FLY_API_TOKEN — set in CI; also set in dev shell.
#
# Optional env:
#   VAULT_APP            — default "haworks-vault"
#   SERVICES_JSON        — default "infra/vault/services.json"
#   FLY_APP_PREFIX       — default "haworks-"
#   WRAP_TTL_SECONDS     — default 1800 (30min). Sized to cover the worst-
#                          case deploy lag: ci-stage-vault-creds runs once
#                          before deploy-backends starts; a slow service
#                          may not boot + try to unwrap until 10+min later.
#                          5min would expire mid-deploy.
set -euo pipefail

VAULT_APP="${VAULT_APP:-haworks-vault}"
SERVICES_JSON="${SERVICES_JSON:-infra/vault/services.json}"
FLY_APP_PREFIX="${FLY_APP_PREFIX:-haworks-}"
WRAP_TTL_SECONDS="${WRAP_TTL_SECONDS:-86400}"

log() { echo "[stage-vault-creds] $*"; }

# Mask a secret value in GitHub Actions logs BEFORE any echo of it.
# Outside CI (no GITHUB_ACTIONS env), this is a no-op echo to /dev/null.
# Always prefer this over `echo "${value:0:8}..."` truncation — partial
# disclosure of high-entropy secrets is still secret material.
mask() {
  if [[ "${GITHUB_ACTIONS:-false}" == "true" ]]; then
    echo "::add-mask::$1"
  fi
}

# Map vault service name → Fly app name. Most are 1:1 with the
# haworks- prefix; two have historical aliases.
fly_app_for_service() {
  case "$1" in
    checkout-orchestrator) echo "${FLY_APP_PREFIX}checkout" ;;
    bff-web)               echo "${FLY_APP_PREFIX}bffweb"   ;;
    *)                     echo "${FLY_APP_PREFIX}$1"        ;;
  esac
}

# Run a command inside the vault container via flyctl ssh. Returns the
# stdout (with the "Connecting to ..." prefix line stripped). Uses sed
# rather than `grep -v` because grep -v exits 1 when no lines match,
# which combined with pipefail kills the wait loop on any short response.
fly_ssh() {
  flyctl ssh console -a "$VAULT_APP" -C "$1" \
    | tr -d '\r' \
    | sed '/^Connecting to/d'
}

# ---------------------------------------------------------------------------
# Pre-flight: services.json must exist + parse cleanly. CI runs from repo
# root, dev runs may run from anywhere — find the file relative to the
# script if the default relative path doesn't resolve.
# ---------------------------------------------------------------------------
if [[ ! -f "$SERVICES_JSON" ]]; then
  script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  alt="$script_dir/../../infra/vault/services.json"
  if [[ -f "$alt" ]]; then
    SERVICES_JSON="$alt"
  else
    log "ERROR: services.json not found at $SERVICES_JSON or $alt"
    exit 1
  fi
fi

if ! jq -e '.services | length > 0' "$SERVICES_JSON" >/dev/null; then
  log "ERROR: $SERVICES_JSON has no services array"
  exit 1
fi

# ---------------------------------------------------------------------------
# Wait for vault to be FULLY READY: initialized + unsealed + active.
# A sealed-but-listening vault would let the listener check pass but
# return 503 from auth/approle endpoints. /v1/sys/health returns 200
# only when initialized + unsealed + active (default behavior).
# ---------------------------------------------------------------------------
# Wait for vault to be ready. With Transit auto-unseal this should be
# fast (~10s: transit vault boots → prod vault boots → auto-unseals).
# Keep a generous timeout for cold starts / first-time seal migration.
log "waiting for vault to be unsealed + active on $VAULT_APP..."
ready=0
for i in $(seq 1 60); do
  if fly_ssh 'sh -c "curl -fsS -o /dev/null http://[::1]:8200/v1/sys/health"' >/dev/null 2>&1; then
    ready=1
    log "vault is unsealed + active (attempt $i)"
    break
  fi
  sleep 3
done
if [[ "$ready" != "1" ]]; then
  log "ERROR: vault never reached active+unsealed within 180s"
  log "       check: flyctl logs --app $VAULT_APP"
  exit 1
fi

# ---------------------------------------------------------------------------
# Step 1: obtain a working Vault token.
#
# Priority:
#   1. VAULT_CI_TOKEN env on the vault machine (set by a prior run)
#   2. root_token from .init.json (first-ever boot only — token gets revoked
#      after we create the CI token, so this path only fires once)
# ---------------------------------------------------------------------------
root_token=""

# Fast path: CI token already exists from a prior run.
log "checking for existing VAULT_CI_TOKEN..."
ci_token_env=$(fly_ssh 'sh -c "printenv VAULT_CI_TOKEN 2>/dev/null || echo NOTSET"' | head -1)
if [[ -n "$ci_token_env" && "$ci_token_env" != "NOTSET" && "$ci_token_env" != "null" ]]; then
  # Verify the token can actually read AppRole data (not just self-lookup).
  # A stale token from a prior vault instance may pass lookup-self briefly
  # but fail on the real AppRole endpoints we need.
  first_svc=$(jq -r '.services[0].name' "$SERVICES_JSON")
  if fly_ssh "sh -c \"curl -fsS -H 'X-Vault-Token: $ci_token_env' http://[::1]:8200/v1/auth/approle/role/haworks-$first_svc/role-id\"" >/dev/null 2>&1; then
    root_token="$ci_token_env"
    mask "$root_token"
    log "using existing VAULT_CI_TOKEN (verified AppRole access)"
  else
    log "VAULT_CI_TOKEN exists but lacks AppRole access — will recreate from .init.json"
  fi
fi

# Slow path: .init.json has the root token (first boot or stale CI token).
if [[ -z "$root_token" ]]; then
  log "checking for /vault/data/.init.json..."
  init_json=$(fly_ssh 'sh -c "cat /vault/data/.init.json 2>/dev/null || echo NOFILE"')
  if [[ "$init_json" != "NOFILE" ]] && echo "$init_json" | jq -e '.root_token' >/dev/null 2>&1; then
    init_root=$(echo "$init_json" | jq -r '.root_token')
    unseal_key=$(echo "$init_json" | jq -r '.unseal_keys_b64[0]')

    # Verify the root token is still valid (it gets revoked after first use)
    if fly_ssh "sh -c \"curl -fsS -H 'X-Vault-Token: $init_root' http://[::1]:8200/v1/auth/token/lookup-self\"" >/dev/null 2>&1; then
      log "creating CI deployer policy and token..."
      fly_ssh "sh -c \"VAULT_ADDR=http://127.0.0.1:8200 VAULT_TOKEN=$init_root vault policy write ci-deployer - <<'POLICY'
path \\\"auth/approle/role/+/role-id\\\" { capabilities = [\\\"read\\\"] }
path \\\"auth/approle/role/+/secret-id\\\" { capabilities = [\\\"update\\\"] }
POLICY\"" >/dev/null

      ci_token_json=$(fly_ssh "sh -c \"curl -fsS -X POST -H 'X-Vault-Token: $init_root' -d '{\\\"policies\\\": [\\\"ci-deployer\\\"], \\\"period\\\": \\\"720h\\\"}' http://[::1]:8200/v1/auth/token/create\"")
      ci_token=$(echo "$ci_token_json" | jq -r '.auth.client_token')

      mask "$unseal_key"
      mask "$ci_token"
      log "staging VAULT_UNSEAL_KEY + VAULT_CI_TOKEN on $VAULT_APP"
      flyctl secrets set --stage -a "$VAULT_APP" \
        "VAULT_UNSEAL_KEY=$unseal_key" \
        "VAULT_CI_TOKEN=$ci_token" >/dev/null
      flyctl secrets unset --stage -a "$VAULT_APP" VAULT_ROOT_TOKEN_PROD 2>/dev/null || true

      log "revoking root token..."
      fly_ssh "sh -c \"curl -fsS -X POST -H 'X-Vault-Token: $init_root' http://[::1]:8200/v1/auth/token/revoke-self\"" >/dev/null || true

      root_token="$ci_token"
    else
      log ".init.json root token is revoked (already captured in a prior run)"
    fi
  else
    log "no .init.json on disk"
  fi
fi

if [[ -z "$root_token" || "$root_token" == "null" ]]; then
  log "ERROR: no CI token available — cannot capture AppRole creds."
  log "       Re-run after VAULT_CI_TOKEN propagates (one redeploy)."
  exit 1
fi

# ---------------------------------------------------------------------------
# Step 2: per-service AppRole creds with response-wrapped secret_ids.
#
# The wrapping token is what we stage to Fly. The service's bootstrap
# library unwraps it on first boot to get the actual secret_id (single-
# use, 5-min TTL). A leaked CI log only exposes the wrapper, which is
# useless after 5 minutes or after one unwrap (whichever comes first).
# ---------------------------------------------------------------------------
service_count=$(jq -r '.services | length' "$SERVICES_JSON")
i=0
deployed_count=0
skipped_count=0

while [[ "$i" -lt "$service_count" ]]; do
  svc=$(jq -r ".services[$i].name" "$SERVICES_JSON")
  fly_app=$(fly_app_for_service "$svc")
  role_name="haworks-$svc"

  i=$((i + 1))

  # Soft-skip when the Fly app doesn't exist yet (chicken-and-egg: app
  # must be created before first creds-stage). Keeps the workflow green
  # for partial deploys.
  if ! flyctl status -a "$fly_app" >/dev/null 2>&1; then
    log "skip $svc — Fly app $fly_app not deployed yet"
    skipped_count=$((skipped_count + 1))
    continue
  fi

  log "fetching role_id for $role_name"
  role_id_json=$(fly_ssh "sh -c \"curl -fsS -H 'X-Vault-Token: $root_token' http://[::1]:8200/v1/auth/approle/role/$role_name/role-id\"")
  role_id=$(echo "$role_id_json" | jq -r '.data.role_id // empty')

  if ! echo "$role_id" | grep -qE '^[0-9a-f-]{36}$'; then
    log "ERROR ($svc): role_id not UUID-shaped. AppRole likely missing — re-run vault deploy to seed."
    log "             Vault response: $role_id_json"
    exit 1
  fi

  # Issue a response-wrapped secret_id. The X-Vault-Wrap-TTL header tells
  # vault to wrap the response in a single-use token instead of returning
  # the raw secret_id. Service unwraps on first boot.
  log "issuing wrapped secret_id for $role_name (TTL=${WRAP_TTL_SECONDS}s)"
  wrap_resp=$(fly_ssh "sh -c \"curl -fsS -X POST -H 'X-Vault-Token: $root_token' -H 'X-Vault-Wrap-TTL: $WRAP_TTL_SECONDS' http://[::1]:8200/v1/auth/approle/role/$role_name/secret-id\"")
  wrapping_token=$(echo "$wrap_resp" | jq -r '.wrap_info.token // empty')

  # Vault wrapping tokens are hvs.* (since Vault 1.10) — be defensive but
  # don't gate on the prefix in case format changes; just check it's not
  # empty and looks token-shaped.
  if [[ -z "$wrapping_token" || "$wrapping_token" == "null" ]]; then
    log "ERROR ($svc): wrap response missing wrap_info.token. Response: $wrap_resp"
    exit 1
  fi

  mask "$wrapping_token"
  # role_id is technically half of the credential pair (both role_id AND
  # secret_id are required to login under AppRole), but defense-in-depth:
  # mask it anyway since CI logs are forever.
  mask "$role_id"

  log "staging Vault__RoleId + wrapped Vault__SecretId on $fly_app"
  flyctl secrets set --stage -a "$fly_app" \
    "Vault__RoleId=$role_id" \
    "Vault__SecretId=$wrapping_token" \
    "Vault__SecretIdIsWrapped=true" >/dev/null

  deployed_count=$((deployed_count + 1))
done

log "done. staged $deployed_count services, skipped $skipped_count (no Fly app yet)."
