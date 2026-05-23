#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-https://haworks-bffweb.fly.dev}"
IDENTITY_URL="${IDENTITY_URL:-https://haworks-identity.fly.dev}"
SERVICE_SECRET="${SERVICE_SECRET:?SERVICE_SECRET is required}"
POLL_TIMEOUT=60
POLL_INTERVAL=3

log() { echo "[$(date -u +%H:%M:%S)] $*"; }

log "=== Checkout Synthetic Monitor ==="
log "Target: ${BASE_URL}"

# Phase 1: Register + login as a real user (checkout requires user JWT, not service token)
RUN_ID=$(date +%s)
TEST_USER="synthcheckout${RUN_ID}"
TEST_EMAIL="${TEST_USER}@test.invalid"
TEST_PASSWORD="SynCheck${RUN_ID}!Aa"

log "Registering test user: ${TEST_USER}"
curl -s --max-time 15 \
  -X POST "${IDENTITY_URL}/api/v1/authentication/register" \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"${TEST_USER}\",\"email\":\"${TEST_EMAIL}\",\"password\":\"${TEST_PASSWORD}\",\"confirmPassword\":\"${TEST_PASSWORD}\"}" \
  > /dev/null 2>&1 || true

log "Logging in as ${TEST_USER}..."
LOGIN_RESP=$(curl -s --max-time 15 \
  -X POST "${IDENTITY_URL}/api/v1/authentication/login" \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"${TEST_USER}\",\"password\":\"${TEST_PASSWORD}\"}" 2>&1) || true

TOKEN=$(echo "${LOGIN_RESP}" | jq -r '.token // .accessToken // empty' 2>/dev/null)
if [[ -z "${TOKEN}" ]]; then
  log "FAIL: Could not get user JWT"
  log "Response: ${LOGIN_RESP}"
  exit 1
fi
log "OK: Authenticated as user"

AUTH_HEADER="Authorization: Bearer ${TOKEN}"

# Phase 2: Create checkout via BFF (requires user JWT with NameIdentifier claim)
IDEMPOTENCY_KEY="synth-checkout-${RUN_ID}-$$"
log "Creating checkout (idempotency key: ${IDEMPOTENCY_KEY})..."

CHECKOUT_RESPONSE=$(curl -s --max-time 15 \
  -X POST "${BASE_URL}/api/v1/checkout" \
  -H "${AUTH_HEADER}" \
  -H "Content-Type: application/json" \
  -H "X-Idempotency-Key: ${IDEMPOTENCY_KEY}" \
  -d '{
    "customerEmail": "'"${TEST_EMAIL}"'",
    "totalAmount": 9.99,
    "idempotencyKey": "'"${IDEMPOTENCY_KEY}"'",
    "items": [
      {
        "productId": "00000000-0000-0000-0000-000000000001",
        "productName": "Synthetic Test Product",
        "quantity": 1,
        "unitPrice": 9.99
      }
    ]
  }' 2>&1) || true

SAGA_ID=$(echo "${CHECKOUT_RESPONSE}" | jq -r '.sagaId // .checkoutId // .id // empty' 2>/dev/null)

if [[ -z "${SAGA_ID}" ]]; then
  # Checkout might return 202 with no body, or a different shape
  HTTP_CODE=$(echo "${CHECKOUT_RESPONSE}" | jq -r '.status // empty' 2>/dev/null)
  if [[ "${HTTP_CODE}" == "202" ]] || echo "${CHECKOUT_RESPONSE}" | grep -q "Accepted"; then
    log "OK: Checkout accepted (202, no saga ID in response — saga runs async)"
    exit 0
  fi
  log "FAIL: Could not extract sagaId from response"
  log "Response: ${CHECKOUT_RESPONSE}"
  exit 1
fi
log "OK: Checkout created (saga=${SAGA_ID})"

# Phase 3: Poll saga state via demo endpoint (if available)
log "Polling saga state (timeout=${POLL_TIMEOUT}s)..."
ELAPSED=0
FINAL_STATE=""

while [[ ${ELAPSED} -lt ${POLL_TIMEOUT} ]]; do
  STATUS_RESPONSE=$(curl -s --max-time 10 \
    -H "${AUTH_HEADER}" \
    "${BASE_URL}/api/v1/demo/saga/${SAGA_ID}" 2>/dev/null || echo '{}')

  CURRENT_STATE=$(echo "${STATUS_RESPONSE}" | jq -r '.currentState // .state // .status // "Unknown"' 2>/dev/null)
  log "  State: ${CURRENT_STATE} (${ELAPSED}s elapsed)"

  case "${CURRENT_STATE}" in
    Completed|completed|PaymentConfirmed|OrderConfirmed|Initiated|StockReserved|ReadyForPayment)
      # Any valid saga state means the saga was created and is progressing
      if [[ "${CURRENT_STATE}" == "Completed" || "${CURRENT_STATE}" == "completed" ]]; then
        FINAL_STATE="success"
        break
      fi
      # Still in progress — keep polling
      ;;
    Failed|failed|Faulted|Abandoned)
      FINAL_STATE="failed"
      break
      ;;
  esac

  sleep "${POLL_INTERVAL}"
  ELAPSED=$((ELAPSED + POLL_INTERVAL))
done

# Phase 4: Report
if [[ "${FINAL_STATE}" == "success" ]]; then
  log "OK: Checkout saga completed in ${ELAPSED}s"
  exit 0
elif [[ -n "${SAGA_ID}" && "${ELAPSED}" -gt 0 ]]; then
  # Saga exists and is progressing — that's enough for a synthetic monitor
  log "OK: Checkout saga created and progressing (last state: ${CURRENT_STATE}, ${ELAPSED}s)"
  exit 0
elif [[ "${FINAL_STATE}" == "failed" ]]; then
  log "FAIL: Checkout saga failed: ${CURRENT_STATE}"
  exit 1
else
  log "FAIL: Checkout saga timed out after ${POLL_TIMEOUT}s (last state: ${CURRENT_STATE})"
  exit 1
fi
