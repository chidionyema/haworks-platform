#!/bin/sh
# Vault entrypoint with Transit auto-unseal.
#
# Architecture (single Fly machine):
#   1. Start a dev-mode "transit vault" on :8100 (never seals, no persistence)
#   2. Ensure the transit vault has a "transit/autounseal" key
#   3. Start the production vault on :8200 with seal "transit" config
#   4. Prod vault auto-unseals via the transit endpoint — zero operator action
#   5. On first-ever boot: `vault operator init -recovery-shares=1`
#   6. Run seed.sh to configure AppRole + policies + DB roles
#
# No Shamir keys. No unseal race. No CI timing issues.
set -e

export VAULT_ADDR="http://127.0.0.1:8200"
TRANSIT_ADDR="http://127.0.0.1:8100"
TRANSIT_TOKEN="transit-autounseal-token"

# ── Step 1: Start transit vault (dev mode) ─────────────────────────
echo "[entrypoint] starting transit vault on :8100..."
VAULT_DEV_ROOT_TOKEN_ID="$TRANSIT_TOKEN" \
VAULT_DEV_LISTEN_ADDRESS="127.0.0.1:8100" \
  vault server -dev -dev-no-store-token -log-level=error &
TRANSIT_PID=$!

# Wait for transit vault to be ready
for i in $(seq 1 15); do
  if curl -fsS -o /dev/null "$TRANSIT_ADDR/v1/sys/health" 2>/dev/null; then
    echo "[entrypoint] transit vault ready"
    break
  fi
  sleep 1
done

# ── Step 2: Ensure transit key exists ──────────────────────────────
# Enable transit engine (idempotent)
curl -fsS -X POST -H "X-Vault-Token: $TRANSIT_TOKEN" \
  -d '{"type": "transit"}' \
  "$TRANSIT_ADDR/v1/sys/mounts/transit" 2>/dev/null || true

# Create the autounseal key (idempotent — "already exists" returns 204)
curl -fsS -X POST -H "X-Vault-Token: $TRANSIT_TOKEN" \
  -d '{"type": "aes256-gcm96"}' \
  "$TRANSIT_ADDR/v1/transit/keys/autounseal" 2>/dev/null || true

echo "[entrypoint] transit autounseal key ready"

# ── Step 3: Start production vault ─────────────────────────────────
# Token is hardcoded in vault.hcl (local dev-mode transit, not a real secret).
# Also export as env var for belt-and-suspenders — some Vault versions read
# the seal token from VAULT_SEAL_TRANSIT_TOKEN instead of the HCL `token`.
export VAULT_SEAL_TRANSIT_TOKEN="$TRANSIT_TOKEN"

# Prevent the Fly-secret VAULT_DEV_ROOT_TOKEN_ID from leaking a confusing
# "cannot specify custom root token ID outside dev mode" warning on prod vault.
unset VAULT_DEV_ROOT_TOKEN_ID 2>/dev/null || true

echo "[entrypoint] starting production vault on :8200..."
vault server -config=/vault/config/vault.hcl -log-level=warn &
VAULT_PID=$!

# Wait for prod vault listener
echo "[entrypoint] waiting for production vault..."
for i in $(seq 1 30); do
  if vault status >/dev/null 2>&1 || [ "$?" -eq 2 ]; then
    echo "[entrypoint] production vault is responsive"
    break
  fi
  sleep 1
done

# ── Step 4: Initialize or migrate ──────────────────────────────────
INIT_FILE=/vault/data/.init.json
MIGRATION_DONE="/vault/data/.transit-migration-done"

if vault status -format=json 2>/dev/null | jq -e '.initialized == false' >/dev/null 2>&1; then
  # Fresh install — init directly with transit seal (recovery keys, not unseal keys)
  echo "[entrypoint] vault is uninitialized — running operator init with recovery keys"
  vault operator init -recovery-shares=1 -recovery-threshold=1 -format=json > "$INIT_FILE"
  chmod 600 "$INIT_FILE"
  touch "$MIGRATION_DONE"
  echo "[entrypoint] operator init complete (auto-unseal active)"

elif [ ! -f "$MIGRATION_DONE" ]; then
  # Existing Shamir-sealed vault needs one-time migration to transit seal.
  # `vault operator unseal -migrate` re-encrypts the master key with the
  # transit backend. Requires the old Shamir unseal key one last time.
  echo "[entrypoint] migrating from Shamir to Transit seal..."

  # Get the Shamir key from env or .init.json
  shamir_key=""
  if [ -n "${VAULT_UNSEAL_KEY:-}" ]; then
    shamir_key="$VAULT_UNSEAL_KEY"
  elif [ -f "$INIT_FILE" ]; then
    shamir_key=$(jq -r '.unseal_keys_b64[0]' "$INIT_FILE" 2>/dev/null)
  fi

  if [ -n "$shamir_key" ] && [ "$shamir_key" != "null" ]; then
    if vault operator unseal -migrate "$shamir_key" >/dev/null 2>&1; then
      touch "$MIGRATION_DONE"
      echo "[entrypoint] seal migration complete — Shamir → Transit"
    else
      echo "[entrypoint] ERROR: seal migration failed — falling back to Shamir unseal"
      vault operator unseal "$shamir_key" >/dev/null 2>&1 || true
    fi
  else
    echo "[entrypoint] ERROR: no Shamir key for migration — vault may remain sealed"
  fi
else
  echo "[entrypoint] transit auto-unseal active (migration already done)"
fi

# ── Step 5: Verify unsealed ────────────────────────────────────────
for i in $(seq 1 10); do
  if vault status -format=json 2>/dev/null | jq -e '.sealed == false' >/dev/null 2>&1; then
    echo "[entrypoint] vault is unsealed"
    break
  fi
  sleep 2
done

if vault status -format=json 2>/dev/null | jq -e '.sealed == true' >/dev/null 2>&1; then
  echo "[entrypoint] ERROR: vault still sealed — check transit vault on :8100"
  wait "$VAULT_PID"
  exit 1
fi

# ── Step 6: Seed (AppRole, policies, DB roles) ─────────────────────
seed_token() {
  # On first boot: root token from .init.json. On subsequent: VAULT_ROOT_TOKEN_PROD env.
  if [ -n "${VAULT_ROOT_TOKEN_PROD:-}" ]; then
    echo "$VAULT_ROOT_TOKEN_PROD"
    return 0
  fi
  if [ -f "$INIT_FILE" ]; then
    jq -r '.root_token' "$INIT_FILE" 2>/dev/null
    return 0
  fi
  return 1
}

if TOKEN="$(seed_token)" && [ -n "$TOKEN" ] && [ "$TOKEN" != "null" ]; then
  # Rotate audit log
  AUDIT_LOG="/vault/data/audit.log"
  if [ -f "$AUDIT_LOG" ]; then
    if VAULT_TOKEN="$TOKEN" vault audit list -format=json 2>/dev/null | jq -e '."file/"' >/dev/null 2>&1; then
      VAULT_TOKEN="$TOKEN" vault audit disable file 2>/dev/null || true
    fi
    rm -f "${AUDIT_LOG}.prev"
    mv "$AUDIT_LOG" "${AUDIT_LOG}.prev"
    echo "[entrypoint] rotated audit log"
  fi
  VAULT_TOKEN="$TOKEN" vault audit enable file file_path="$AUDIT_LOG" >/dev/null 2>&1 || true

  echo "[entrypoint] running seed.sh"
  if VAULT_TOKEN="$TOKEN" /usr/local/bin/seed.sh; then
    echo "[entrypoint] seed complete — vault is fully ready"
  else
    echo "[entrypoint] WARN: seed.sh failed (vault still serving)"
  fi
else
  echo "[entrypoint] WARN: no token — skipping seed"
fi

# Stay attached to prod vault (transit vault runs as background child).
wait "$VAULT_PID"
