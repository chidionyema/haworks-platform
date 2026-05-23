#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-https://haworks-bffweb.fly.dev}"
SERVICE_SECRET="${SERVICE_SECRET:?SERVICE_SECRET is required}"

log() { echo "[$(date -u +%H:%M:%S)] $*"; }

log "=== Ledger Synthetic Monitor ==="
log "Target: ${BASE_URL}"

# Phase 1: Authenticate
log "Authenticating via service token..."
AUTH_RESPONSE=$(curl -s --max-time 10 \
  -X POST "${BASE_URL}/api/v1/authentication/service-token" \
  -H "Content-Type: application/json" \
  -d "{\"secret\": \"${SERVICE_SECRET}\"}")

TOKEN=$(echo "${AUTH_RESPONSE}" | jq -r '.token // .accessToken // empty')
if [[ -z "${TOKEN}" ]]; then
  log "FAIL: Could not extract token from auth response"
  log "Response: ${AUTH_RESPONSE}"
  exit 1
fi
log "OK: Authenticated successfully"

AUTH_HEADER="Authorization: Bearer ${TOKEN}"

# Phase 2: Get current balance for test seller
SELLER_ID="synth-seller-$(date -u +%Y%m%d)-monitor"
log "Fetching current balance for seller: ${SELLER_ID}..."

BALANCE_BEFORE_RESPONSE=$(curl -s --max-time 5 \
  -H "${AUTH_HEADER}" \
  "${BASE_URL}/api/v1/payouts/ledger/balance/${SELLER_ID}" 2>/dev/null || echo '{}')

BALANCE_BEFORE=$(echo "${BALANCE_BEFORE_RESPONSE}" | jq -r '.balanceCents // .balance // "0"')
log "Current balance: ${BALANCE_BEFORE} cents"

# Phase 3: Credit the ledger
CREDIT_AMOUNT=100
IDEMPOTENCY_KEY="synth-ledger-$(date -u +%Y%m%d%H%M%S)-$$"
log "Crediting ledger with ${CREDIT_AMOUNT} cents (idempotency key: ${IDEMPOTENCY_KEY})..."

START_TIME=$(date +%s%N)
CREDIT_RESPONSE=$(curl -s --max-time 5 \
  -X POST "${BASE_URL}/api/v1/payouts/ledger/credit" \
  -H "${AUTH_HEADER}" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: ${IDEMPOTENCY_KEY}" \
  -d "{
    \"sellerId\": \"${SELLER_ID}\",
    \"amountCents\": ${CREDIT_AMOUNT},
    \"currency\": \"usd\",
    \"description\": \"Synthetic monitor test credit\",
    \"isTest\": true
  }")
END_TIME=$(date +%s%N)
ELAPSED=$(echo "scale=2; (${END_TIME} - ${START_TIME}) / 1000000000" | bc -l)

if (( $(echo "${ELAPSED} > 5" | bc -l) )); then
  log "FAIL: Ledger credit took ${ELAPSED}s (max 5s)"
  exit 1
fi
log "OK: Ledger credited in ${ELAPSED}s"

# Phase 4: Verify updated balance
log "Verifying updated balance..."
START_TIME=$(date +%s%N)
BALANCE_AFTER_RESPONSE=$(curl -s --max-time 5 \
  -H "${AUTH_HEADER}" \
  "${BASE_URL}/api/v1/payouts/ledger/balance/${SELLER_ID}")
END_TIME=$(date +%s%N)
ELAPSED=$(echo "scale=2; (${END_TIME} - ${START_TIME}) / 1000000000" | bc -l)

BALANCE_AFTER=$(echo "${BALANCE_AFTER_RESPONSE}" | jq -r '.balanceCents // .balance // "0"')
EXPECTED_BALANCE=$((BALANCE_BEFORE + CREDIT_AMOUNT))

if (( $(echo "${ELAPSED} > 5" | bc -l) )); then
  log "FAIL: Balance check took ${ELAPSED}s (max 5s)"
  exit 1
fi

log "Balance before: ${BALANCE_BEFORE}, after: ${BALANCE_AFTER}, expected: ${EXPECTED_BALANCE}"

if [[ "${BALANCE_AFTER}" -ne "${EXPECTED_BALANCE}" ]]; then
  log "FAIL: Balance mismatch — expected ${EXPECTED_BALANCE}, got ${BALANCE_AFTER}"
  exit 1
fi

log "OK: Ledger balance verified (${BALANCE_AFTER} cents)"
exit 0
