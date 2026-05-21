#!/bin/sh
# Vault entrypoint with Transit auto-unseal (persistent transit backend).
#
# Architecture (single Fly machine):
#   1. Start a transit vault on :8100 with persistent file backend at
#      /vault/data/transit/ on the Fly volume. The encryption key survives
#      container restarts (unlike the old dev-mode approach).
#   2. Ensure the transit vault has a "transit/autounseal" key
#   3. Start the production vault on :8200 with seal "transit" config
#   4. Prod vault auto-unseals via the transit endpoint
#   5. On first-ever boot: vault operator init (recovery keys)
#   6. Run seed.sh to configure AppRole + policies + DB roles
#
# One-time migration: if the prod vault's raft data was encrypted with a
# now-lost dev-mode transit key, the entrypoint wipes raft and re-inits.
set -e

export VAULT_ADDR="http://127.0.0.1:8200"
TRANSIT_ADDR="http://127.0.0.1:8100"
TRANSIT_DATA="/vault/data/transit"
INIT_FILE=/vault/data/.init.json
MIGRATION_MARKER="/vault/data/.persistent-transit-ok"

# Prevent stale Fly secrets from confusing vault
unset VAULT_DEV_ROOT_TOKEN_ID 2>/dev/null || true

# ── Step 1: Start transit vault with persistent file backend ───────
mkdir -p "$TRANSIT_DATA"
echo "[entrypoint] starting transit vault on :8100 (data at $TRANSIT_DATA)..."
vault server -config=/vault/config/transit-vault.hcl -log-level=error &
TRANSIT_PID=$!

# Wait for transit vault — it needs init+unseal on first boot (file backend)
for attempt in $(seq 1 30); do
  http_code=$(curl -sS -o /dev/null -w '%{http_code}' "$TRANSIT_ADDR/v1/sys/health" 2>/dev/null) || http_code="000"

  if [ "$http_code" = "200" ]; then
    echo "[entrypoint] transit vault ready (attempt $attempt)"
    break
  elif [ "$http_code" = "501" ]; then
    # 501 = not initialized
    echo "[entrypoint] initializing transit vault..."
    transit_init=$(curl -sS -X PUT -d '{"secret_shares":1,"secret_threshold":1}' \
      "$TRANSIT_ADDR/v1/sys/init" 2>/dev/null)
    echo "$transit_init" | jq -r '.keys_base64[0]' > "$TRANSIT_DATA/.unseal-key"
    echo "$transit_init" | jq -r '.root_token' > "$TRANSIT_DATA/.root-token"
    chmod 600 "$TRANSIT_DATA/.unseal-key" "$TRANSIT_DATA/.root-token"
    # Unseal it
    curl -sS -X PUT -d "{\"key\":\"$(cat "$TRANSIT_DATA/.unseal-key")\"}" \
      "$TRANSIT_ADDR/v1/sys/unseal" >/dev/null 2>&1
    echo "[entrypoint] transit vault initialized + unsealed"
  elif [ "$http_code" = "503" ]; then
    # 503 = sealed
    if [ -f "$TRANSIT_DATA/.unseal-key" ]; then
      echo "[entrypoint] unsealing transit vault..."
      curl -sS -X PUT -d "{\"key\":\"$(cat "$TRANSIT_DATA/.unseal-key")\"}" \
        "$TRANSIT_ADDR/v1/sys/unseal" >/dev/null 2>&1
    fi
  fi
  sleep 1
done

# Final health check
if ! curl -fsS -o /dev/null "$TRANSIT_ADDR/v1/sys/health" 2>/dev/null; then
  echo "[entrypoint] ERROR: transit vault not healthy after 30s"
  kill "$TRANSIT_PID" 2>/dev/null || true
  exit 1
fi

# ── Step 2: Ensure transit autounseal key exists ───────────────────
TRANSIT_TOKEN=$(cat "$TRANSIT_DATA/.root-token" 2>/dev/null || echo "")
if [ -z "$TRANSIT_TOKEN" ]; then
  echo "[entrypoint] ERROR: no transit root token found"
  exit 1
fi

# Enable transit engine (idempotent — 400 if already enabled)
curl -fsS -X POST -H "X-Vault-Token: $TRANSIT_TOKEN" \
  -d '{"type": "transit"}' \
  "$TRANSIT_ADDR/v1/sys/mounts/transit" 2>/dev/null || true

# Create autounseal key (idempotent)
curl -fsS -X POST -H "X-Vault-Token: $TRANSIT_TOKEN" \
  -d '{"type": "aes256-gcm96"}' \
  "$TRANSIT_ADDR/v1/transit/keys/autounseal" 2>/dev/null || true

echo "[entrypoint] transit autounseal key ready"

# ── Step 3: Start production vault ─────────────────────────────────
# Inject transit token into vault.hcl at runtime
sed -i "s|token.*=.*\".*\"|token = \"$TRANSIT_TOKEN\"|" /vault/config/vault.hcl

echo "[entrypoint] starting production vault on :8200..."
vault server -config=/vault/config/vault.hcl -log-level=warn &
VAULT_PID=$!

echo "[entrypoint] waiting for production vault..."
for i in $(seq 1 30); do
  vault status >/dev/null 2>&1
  rc=$?
  if [ "$rc" -eq 0 ] || [ "$rc" -eq 2 ]; then
    echo "[entrypoint] production vault is responsive"
    break
  fi
  sleep 1
done

# ── Step 4: Initialize or recover from stale seal ──────────────────
if vault status -format=json 2>/dev/null | jq -e '.initialized == false' >/dev/null 2>&1; then
  echo "[entrypoint] vault is uninitialized — running operator init (recovery keys)"
  vault operator init -recovery-shares=1 -recovery-threshold=1 -format=json > "$INIT_FILE"
  chmod 600 "$INIT_FILE"
  touch "$MIGRATION_MARKER"
  echo "[entrypoint] operator init complete"
fi

# Wait for transit auto-unseal
unsealed=0
for i in $(seq 1 20); do
  if vault status -format=json 2>/dev/null | jq -e '.sealed == false' >/dev/null 2>&1; then
    echo "[entrypoint] vault is unsealed"
    unsealed=1
    touch "$MIGRATION_MARKER"
    break
  fi
  sleep 2
done

# If still sealed and no migration marker, raft data is from a lost transit
# key (dev-mode era). Wipe raft and re-init with the new persistent transit.
if [ "$unsealed" != "1" ] && [ ! -f "$MIGRATION_MARKER" ]; then
  echo "[entrypoint] WARN: sealed with stale transit key — wiping raft + re-initializing"
  echo "[entrypoint]       (one-time migration from dev-mode to persistent transit)"

  kill "$VAULT_PID" 2>/dev/null || true
  wait "$VAULT_PID" 2>/dev/null || true

  # Wipe prod vault data only (transit data in /vault/data/transit/ is kept)
  rm -rf /vault/data/raft /vault/data/vault.db
  rm -f "$INIT_FILE" /vault/data/.transit-migration-done

  vault server -config=/vault/config/vault.hcl -log-level=warn &
  VAULT_PID=$!

  for i in $(seq 1 20); do
    vault status >/dev/null 2>&1
    rc=$?
    if [ "$rc" -eq 0 ] || [ "$rc" -eq 2 ]; then break; fi
    sleep 1
  done

  echo "[entrypoint] re-initializing vault..."
  vault operator init -recovery-shares=1 -recovery-threshold=1 -format=json > "$INIT_FILE"
  chmod 600 "$INIT_FILE"
  touch "$MIGRATION_MARKER"

  for i in $(seq 1 20); do
    if vault status -format=json 2>/dev/null | jq -e '.sealed == false' >/dev/null 2>&1; then
      echo "[entrypoint] vault is unsealed (after re-init)"
      unsealed=1
      break
    fi
    sleep 2
  done
fi

if [ "$unsealed" != "1" ]; then
  echo "[entrypoint] ERROR: vault still sealed"
  vault status 2>&1 || true
  wait "$VAULT_PID"
  exit 1
fi

# ── Step 5: Seed (AppRole, policies, DB roles) ─────────────────────
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

# Stay attached to prod vault (transit vault runs as background child)
wait "$VAULT_PID"
