#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-https://haworks-bffweb.fly.dev}"
SERVICE_SECRET="${SERVICE_SECRET:?SERVICE_SECRET is required}"
POLL_TIMEOUT=60
POLL_INTERVAL=3

log() { echo "[$(date -u +%H:%M:%S)] $*"; }

log "=== Refund Synthetic Monitor ==="
log "Target: ${BASE_URL}"

# Phase 1: Authenticate
log "Authenticating via service token..."
AUTH_RESPONSE=$(curl -s --max-time 10 \
  -X POST "${IDENTITY_URL:-https://haworks-identity.fly.dev}/api/v1/authentication/service-token" \
  -H "X-Service-Secret: ${SERVICE_SECRET}" 2>&1) || true

TOKEN=$(echo "${AUTH_RESPONSE}" | jq -r '.token // .accessToken // empty')
if [[ -z "${TOKEN}" ]]; then
  log "FAIL: Could not extract token from auth response"
  log "Response: ${AUTH_RESPONSE}"
  exit 1
fi
log "OK: Authenticated successfully"

AUTH_HEADER="Authorization: Bearer ${TOKEN}"

# Phase 2: Create a refund request
IDEMPOTENCY_KEY="synth-refund-$(date -u +%Y%m%d%H%M%S)-$$"
log "Creating refund request (idempotency key: ${IDEMPOTENCY_KEY})..."

REFUND_RESPONSE=$(curl -s --max-time 10 \
  -X POST "${BASE_URL}/api/v1/refunds" \
  -H "${AUTH_HEADER}" \
  -H "X-Service-Secret: ${SERVICE_SECRET}" 2>&1) || true
  -H "Idempotency-Key: ${IDEMPOTENCY_KEY}" \
  -d '{
    "orderId": "00000000-0000-0000-0000-000000000001",
    "amountCents": 100,
    "reason": "synthetic_monitor_test",
    "isTest": true
  }')

REFUND_ID=$(echo "${REFUND_RESPONSE}" | jq -r '.refundId // .id // empty')
if [[ -z "${REFUND_ID}" ]]; then
  log "FAIL: Could not extract refundId from response"
  log "Response: ${REFUND_RESPONSE}"
  exit 1
fi
log "OK: Refund request created (id=${REFUND_ID})"

# Phase 3: Poll refund state until terminal
log "Polling refund state (timeout=${POLL_TIMEOUT}s, interval=${POLL_INTERVAL}s)..."
ELAPSED=0
FINAL_STATE=""

while [[ ${ELAPSED} -lt ${POLL_TIMEOUT} ]]; do
  STATUS_RESPONSE=$(curl -s --max-time 10 \
    -H "${AUTH_HEADER}" \
    "${BASE_URL}/api/v1/refunds/${REFUND_ID}" 2>/dev/null || echo '{}')

  CURRENT_STATE=$(echo "${STATUS_RESPONSE}" | jq -r '.state // .status // "Unknown"')
  log "  State: ${CURRENT_STATE} (${ELAPSED}s elapsed)"

  case "${CURRENT_STATE}" in
    Refunded|refunded|Completed|completed)
      FINAL_STATE="success"
      break
      ;;
    RequiresReview|requires_review)
      FINAL_STATE="review"
      break
      ;;
    Failed|failed|Rejected|rejected)
      FINAL_STATE="failed_terminal"
      break
      ;;
  esac

  sleep "${POLL_INTERVAL}"
  ELAPSED=$((ELAPSED + POLL_INTERVAL))
done

# Phase 4: Report
case "${FINAL_STATE}" in
  success)
    log "OK: Refund saga completed successfully in ${ELAPSED}s (state: ${CURRENT_STATE})"
    exit 0
    ;;
  review)
    log "OK: Refund saga reached review state in ${ELAPSED}s (not stuck)"
    exit 0
    ;;
  failed_terminal)
    log "OK: Refund saga reached terminal failure state in ${ELAPSED}s (state: ${CURRENT_STATE})"
    log "NOTE: Terminal failure is acceptable — saga is not stuck"
    exit 0
    ;;
  *)
    log "FAIL: Refund saga timed out after ${POLL_TIMEOUT}s (last state: ${CURRENT_STATE})"
    exit 1
    ;;
esac
