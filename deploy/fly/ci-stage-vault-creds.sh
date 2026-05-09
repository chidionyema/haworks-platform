#!/usr/bin/env bash
# Capture vault's init keys + a fresh AppRole secret_id and stage them
# as Fly secrets on the relevant apps. Designed to be called from CI
# (.github/workflows/deploy.yml's stage-vault-creds job) but safe to
# run from a developer laptop too.
#
# What it does:
#   1. Wait for vault's HTTP listener to come up (up to 60s).
#   2. If /vault/data/.init.json is present (only on first-ever boot),
#      read unseal_keys_b64[0] + root_token; stage them on the vault app
#      so future restarts auto-unseal. Skipped if VAULT_UNSEAL_KEY is
#      already a Fly secret on vault (the file is rotated out for
#      security after capture in some flows).
#   3. With the root token in hand, read auth/approle/role/haworks-
#      identity-app/role-id and write a fresh secret_id; stage both as
#      Vault__RoleId / Vault__SecretId on identity. Identity boots with
#      direct creds — no startup round-trip to vault, no boot race.
#
# Idempotent. Safe to re-run on every deploy.
#
# Required env:
#   FLY_API_TOKEN — set in CI by the workflow; also set in dev shell.
#
# Optional env (overrides):
#   VAULT_APP      — default "ritualworks-vault"
#   IDENTITY_APP   — default "ritualworks-identity"
#   ROLE_NAME      — default "haworks-identity-app"
set -euo pipefail

VAULT_APP="${VAULT_APP:-ritualworks-vault}"
IDENTITY_APP="${IDENTITY_APP:-ritualworks-identity}"
ROLE_NAME="${ROLE_NAME:-haworks-identity-app}"

log() { echo "[stage-vault-creds] $*"; }
fly_ssh() {
  # Quiet wrapper: strips flyctl's "Connecting to ..." prefix and CRs.
  flyctl ssh console -a "$VAULT_APP" -C "$1" 2>/dev/null \
    | tr -d '\r' \
    | sed -n '/^Connecting/!p'
}

# Wait for vault to be alive (sealed-but-listening counts).
log "waiting for vault listener on $VAULT_APP..."
ready=0
for i in $(seq 1 30); do
  status_json=$(fly_ssh 'curl -fsS http://[::1]:8200/v1/sys/health\?standbyok\=true\&sealedcode\=200\&uninitcode\=200' || true)
  if [[ -n "$status_json" ]]; then
    ready=1
    break
  fi
  sleep 2
done
if [[ "$ready" != "1" ]]; then
  log "ERROR: vault listener never came up — is the vault deploy healthy?"
  exit 1
fi

# Step 1: capture init keys (only present on first-ever boot).
log "checking for /vault/data/.init.json..."
init_json=$(fly_ssh 'cat /vault/data/.init.json' || true)
if [[ -n "$init_json" ]] && echo "$init_json" | jq -e '.unseal_keys_b64' >/dev/null 2>&1; then
  unseal_key=$(echo "$init_json" | jq -r '.unseal_keys_b64[0]')
  root_token=$(echo "$init_json" | jq -r '.root_token')
  if [[ -n "$unseal_key" && "$unseal_key" != "null" ]]; then
    log "staging VAULT_UNSEAL_KEY + VAULT_ROOT_TOKEN_PROD on $VAULT_APP"
    flyctl secrets set --stage -a "$VAULT_APP" \
      "VAULT_UNSEAL_KEY=$unseal_key" \
      "VAULT_ROOT_TOKEN_PROD=$root_token" >/dev/null
  fi
else
  log "no .init.json on disk (already captured in a prior run, or vault is sealed) — proceeding to AppRole capture"
fi

# Step 2: AppRole role_id + fresh secret_id. We need a root token for this.
# Prefer the Fly secret VAULT_ROOT_TOKEN_PROD that's now staged on vault;
# fall back to root_token from .init.json if we just captured it.
if [[ -z "${root_token:-}" ]]; then
  log "no root token in this run's context — fetching from vault env"
  root_token=$(fly_ssh 'printenv VAULT_ROOT_TOKEN_PROD' | tail -1 || true)
fi

if [[ -z "${root_token:-}" || "$root_token" == "null" ]]; then
  log "ERROR: no root token available — cannot capture AppRole creds. Re-run after staged VAULT_ROOT_TOKEN_PROD propagates (one redeploy)."
  exit 1
fi

log "fetching role_id + writing fresh secret_id"
role_id=$(fly_ssh "sh -c 'export VAULT_ADDR=http://[::1]:8200 VAULT_TOKEN=$root_token; vault read -field=role_id auth/approle/role/$ROLE_NAME/role-id'" \
  | tail -1 | tr -d ' ')
secret_id=$(fly_ssh "sh -c 'export VAULT_ADDR=http://[::1]:8200 VAULT_TOKEN=$root_token; vault write -force -field=secret_id auth/approle/role/$ROLE_NAME/secret-id'" \
  | tail -1 | tr -d ' ')

if ! echo "$role_id" | grep -qE '^[0-9a-f-]{36}$'; then
  log "ERROR: role_id ('${role_id:0:20}...') is not UUID-shaped — refusing to stage garbage on identity"
  exit 1
fi
if ! echo "$secret_id" | grep -qE '^[0-9a-f-]{36}$'; then
  log "ERROR: secret_id ('${secret_id:0:20}...') is not UUID-shaped — refusing to stage garbage on identity"
  exit 1
fi

log "staging Vault__RoleId + Vault__SecretId on $IDENTITY_APP (role_id=${role_id:0:8}..., secret_id=${secret_id:0:8}...)"
flyctl secrets set --stage -a "$IDENTITY_APP" \
  "Vault__RoleId=$role_id" \
  "Vault__SecretId=$secret_id" >/dev/null

log "done. Identity will pick up new creds on next deploy."
