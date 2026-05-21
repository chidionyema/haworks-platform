#!/bin/sh
# Vault entrypoint with Shamir auto-unseal from persistent volume.
#
# Architecture (single Fly machine):
#   1. Start production vault on :8200 (plain Shamir seal)
#   2. If sealed + .init.json exists on volume, auto-unseal with stored key
#   3. On first-ever boot: `vault operator init`, save keys to volume
#   4. Run seed.sh to configure AppRole + policies + DB roles
#
# The unseal key lives on the Fly persistent volume at /vault/data/.init.json.
# Acceptable for a portfolio/demo platform. Production: use cloud KMS.
set -e

export VAULT_ADDR="http://127.0.0.1:8200"
INIT_FILE=/vault/data/.init.json

# Prevent stale Fly secrets from confusing vault
unset VAULT_DEV_ROOT_TOKEN_ID 2>/dev/null || true
unset VAULT_SEAL_TRANSIT_TOKEN 2>/dev/null || true

# ── Pre-flight: wipe raft data from failed transit seal era ────────
# Old raft data encrypted with a lost transit key causes vault to crash
# on start. Wipe it to force a clean re-initialization.
if [ -d /vault/data/raft ] || [ -f /vault/data/.transit-migration-done ]; then
  # Check if vault can start with existing data by doing a quick probe
  vault server -config=/vault/config/vault.hcl -log-level=error &
  probe_pid=$!
  sleep 5
  rc=0; vault status >/dev/null 2>&1 || rc=$?
  if [ "$rc" -ne 0 ] && [ "$rc" -ne 2 ]; then
    # Vault couldn't start (likely seal mismatch) — wipe and re-init
    echo "[entrypoint] vault failed to start with existing data — wiping for re-init"
    kill "$probe_pid" 2>/dev/null || true
    wait "$probe_pid" 2>/dev/null || true
    rm -rf /vault/data/raft /vault/data/vault.db /vault/data/core
    rm -rf /vault/data/logical /vault/data/sys /vault/data/transit
    rm -f /vault/data/.transit-migration-done /vault/data/.persistent-transit-ok
    rm -f "$INIT_FILE"
    echo "[entrypoint] stale data wiped"
  else
    kill "$probe_pid" 2>/dev/null || true
    wait "$probe_pid" 2>/dev/null || true
  fi
fi

# ── Step 1: Start production vault ─────────────────────────────────
echo "[entrypoint] starting vault on :8200..."
vault server -config=/vault/config/vault.hcl -log-level=warn &
VAULT_PID=$!

echo "[entrypoint] waiting for vault to be responsive..."
for i in $(seq 1 30); do
  vault status >/dev/null 2>&1
  rc=$?
  if [ "$rc" -eq 0 ] || [ "$rc" -eq 2 ]; then
    echo "[entrypoint] vault is responsive (attempt $i)"
    break
  fi
  sleep 1
done

# ── Step 2: Initialize or unseal ───────────────────────────────────
if vault status -format=json 2>/dev/null | jq -e '.initialized == false' >/dev/null 2>&1; then
  echo "[entrypoint] vault is uninitialized — running operator init"
  vault operator init -key-shares=1 -key-threshold=1 -format=json > "$INIT_FILE"
  chmod 600 "$INIT_FILE"
  echo "[entrypoint] operator init complete"
fi

if vault status -format=json 2>/dev/null | jq -e '.sealed == true' >/dev/null 2>&1; then
  unseal_key=""
  if [ -f "$INIT_FILE" ]; then
    unseal_key=$(jq -r '.unseal_keys_b64[0]' "$INIT_FILE" 2>/dev/null)
  fi
  if [ -z "$unseal_key" ] || [ "$unseal_key" = "null" ]; then
    unseal_key="${VAULT_UNSEAL_KEY:-}"
  fi
  if [ -n "$unseal_key" ]; then
    echo "[entrypoint] auto-unsealing..."
    vault operator unseal "$unseal_key" >/dev/null 2>&1
  fi
fi

# ── Step 3: Verify unsealed ────────────────────────────────────────
for i in $(seq 1 10); do
  if vault status -format=json 2>/dev/null | jq -e '.sealed == false' >/dev/null 2>&1; then
    echo "[entrypoint] vault is unsealed"
    break
  fi
  sleep 2
done

if vault status -format=json 2>/dev/null | jq -e '.sealed == true' >/dev/null 2>&1; then
  echo "[entrypoint] ERROR: vault still sealed"
  wait "$VAULT_PID"
  exit 1
fi

# ── Step 4: Seed (AppRole, policies, DB roles) ─────────────────────
seed_token() {
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

wait "$VAULT_PID"
