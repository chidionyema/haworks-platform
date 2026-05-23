#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-https://haworks-bffweb.fly.dev}"
SERVICE_SECRET="${SERVICE_SECRET:?SERVICE_SECRET is required}"
POLL_TIMEOUT=60
POLL_INTERVAL=3

log() { echo "[$(date -u +%H:%M:%S)] $*"; }

log "=== Checkout Synthetic Monitor ==="
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

# Phase 2: Create checkout
IDEMPOTENCY_KEY="synth-checkout-$(date -u +%Y%m%d%H%M%S)-$$"
log "Creating checkout (idempotency key: ${IDEMPOTENCY_KEY})..."

CHECKOUT_RESPONSE=$(curl -s --max-time 10 \
  -X POST "${BASE_URL}/api/v1/checkouts" \
  -H "${AUTH_HEADER}" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: ${IDEMPOTENCY_KEY}" \
  -d '{
    "items": [
      {
        "productId": "00000000-0000-0000-0000-000000000001",
        "quantity": 1,
        "amountCents": 100
      }
    ],
    "currency": "usd",
    "isTest": true
  }')

CHECKOUT_ID=$(echo "${CHECKOUT_RESPONSE}" | jq -r '.checkoutId // .id // empty')
SAGA_ID=$(echo "${CHECKOUT_RESPONSE}" | jq -r '.sagaId // .correlationId // empty')

if [[ -z "${CHECKOUT_ID}" ]]; then
  log "FAIL: Could not extract checkoutId from response"
  log "Response: ${CHECKOUT_RESPONSE}"
  exit 1
fi
log "OK: Checkout created (id=${CHECKOUT_ID}, saga=${SAGA_ID})"

# Phase 3: Poll saga state
log "Polling saga state (timeout=${POLL_TIMEOUT}s, interval=${POLL_INTERVAL}s)..."
ELAPSED=0
FINAL_STATE=""

while [[ ${ELAPSED} -lt ${POLL_TIMEOUT} ]]; do
  POLL_URL="${BASE_URL}/api/v1/checkouts/${CHECKOUT_ID}/status"
  STATUS_RESPONSE=$(curl -s --max-time 10 \
    -H "${AUTH_HEADER}" \
    "${POLL_URL}" 2>/dev/null || echo '{}')

  CURRENT_STATE=$(echo "${STATUS_RESPONSE}" | jq -r '.state // .status // "Unknown"')
  log "  State: ${CURRENT_STATE} (${ELAPSED}s elapsed)"

  case "${CURRENT_STATE}" in
    Completed|completed|PaymentConfirmed|OrderConfirmed)
      FINAL_STATE="success"
      break
      ;;
    Failed|failed|Faulted|Cancelled|cancelled)
      FINAL_STATE="failed"
      break
      ;;
  esac

  sleep "${POLL_INTERVAL}"
  ELAPSED=$((ELAPSED + POLL_INTERVAL))
done

# Phase 4: Report
if [[ "${FINAL_STATE}" == "success" ]]; then
  log "OK: Checkout saga completed successfully in ${ELAPSED}s"
  exit 0
elif [[ "${FINAL_STATE}" == "failed" ]]; then
  log "FAIL: Checkout saga ended in failure state: ${CURRENT_STATE}"
  exit 1
else
  log "FAIL: Checkout saga timed out after ${POLL_TIMEOUT}s (last state: ${CURRENT_STATE})"
  exit 1
fi
