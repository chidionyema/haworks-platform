#!/bin/sh
# Vault entrypoint with Shamir auto-unseal from persistent volume.
set -e

export VAULT_ADDR="http://127.0.0.1:8200"
INIT_FILE=/vault/data/.init.json

# Prevent stale Fly secrets from confusing vault
unset VAULT_DEV_ROOT_TOKEN_ID 2>/dev/null || true
unset VAULT_SEAL_TRANSIT_TOKEN 2>/dev/null || true

# ── Pre-flight: clean up any transit-era data ──────────────────────
# If .init.json is missing, the vault was wiped but leftover files may
# prevent a clean start. Ensure a clean slate.
if [ ! -f "$INIT_FILE" ]; then
  echo "[entrypoint] no .init.json — ensuring clean raft directory"
  rm -rf /vault/data/raft /vault/data/vault.db /vault/data/core
  rm -rf /vault/data/logical /vault/data/sys /vault/data/transit
  rm -f /vault/data/.transit-migration-done /vault/data/.persistent-transit-ok
fi

# ── Step 1: Start vault ────────────────────────────────────────────
echo "[entrypoint] starting vault on :8200..."
vault server -config=/vault/config/vault.hcl -log-level=warn &
VAULT_PID=$!

echo "[entrypoint] waiting for vault..."
for i in $(seq 1 30); do
  rc=0; vault status >/dev/null 2>&1 || rc=$?
  if [ "$rc" -eq 0 ] || [ "$rc" -eq 2 ]; then
    echo "[entrypoint] vault is responsive (attempt $i)"
    break
  fi
  sleep 1
done

# ── Step 2: Initialize or unseal ───────────────────────────────────
rc=0; vault status -format=json 2>/dev/null | jq -e '.initialized == false' >/dev/null 2>&1 || rc=$?
if [ "$rc" -eq 0 ]; then
  echo "[entrypoint] vault is uninitialized — running operator init"
  vault operator init -key-shares=1 -key-threshold=1 -format=json > "$INIT_FILE"
  chmod 600 "$INIT_FILE"
  echo "[entrypoint] operator init complete"
fi

rc=0; vault status -format=json 2>/dev/null | jq -e '.sealed == true' >/dev/null 2>&1 || rc=$?
if [ "$rc" -eq 0 ]; then
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
unsealed=0
for i in $(seq 1 10); do
  rc=0; vault status -format=json 2>/dev/null | jq -e '.sealed == false' >/dev/null 2>&1 || rc=$?
  if [ "$rc" -eq 0 ]; then
    echo "[entrypoint] vault is unsealed"
    unsealed=1
    break
  fi
  sleep 2
done

if [ "$unsealed" != "1" ]; then
  echo "[entrypoint] ERROR: vault still sealed"
  wait "$VAULT_PID"
  exit 1
fi

# ── Step 4: Seed ───────────────────────────────────────────────────
seed_token() {
  if [ -n "${VAULT_ROOT_TOKEN_PROD:-}" ]; then
    echo "$VAULT_ROOT_TOKEN_PROD"; return 0
  fi
  if [ -f "$INIT_FILE" ]; then
    jq -r '.root_token' "$INIT_FILE" 2>/dev/null; return 0
  fi
  return 1
}

if TOKEN="$(seed_token)" && [ -n "$TOKEN" ] && [ "$TOKEN" != "null" ]; then
  AUDIT_LOG="/vault/data/audit.log"
  if [ -f "$AUDIT_LOG" ]; then
    VAULT_TOKEN="$TOKEN" vault audit disable file 2>/dev/null || true
    rm -f "${AUDIT_LOG}.prev"
    mv "$AUDIT_LOG" "${AUDIT_LOG}.prev" 2>/dev/null || true
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
