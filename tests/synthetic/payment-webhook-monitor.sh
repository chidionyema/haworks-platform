#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-https://haworks-bffweb.fly.dev}"
SERVICE_SECRET="${SERVICE_SECRET:?SERVICE_SECRET is required}"
STRIPE_WEBHOOK_SECRET="${STRIPE_WEBHOOK_SECRET:?STRIPE_WEBHOOK_SECRET is required}"
POLL_TIMEOUT=30
POLL_INTERVAL=3

log() { echo "[$(date -u +%H:%M:%S)] $*"; }

log "=== Payment Webhook Synthetic Monitor ==="
log "Target: ${BASE_URL}"

# Phase 1: Authenticate
log "Authenticating..."
AUTH_RESPONSE=$(curl -s --max-time 10 \
  -X POST "${IDENTITY_URL:-https://haworks-identity.fly.dev}/api/v1/authentication/service-token" \
  -H "X-Service-Secret: ${SERVICE_SECRET}" 2>&1) || true

TOKEN=$(echo "${AUTH_RESPONSE}" | jq -r '.token // .accessToken // empty')
if [[ -z "${TOKEN}" ]]; then
  log "FAIL: Authentication failed"
  exit 1
fi
log "OK: Authenticated"

AUTH_HEADER="Authorization: Bearer ${TOKEN}"

# Phase 2: Create a test payment session
IDEMPOTENCY_KEY="synth-pay-$(date -u +%Y%m%d%H%M%S)-$$"
log "Creating test payment session..."

SESSION_RESPONSE=$(curl -s --max-time 10 \
  -X POST "${BASE_URL}/api/v1/payments/sessions" \
  -H "${AUTH_HEADER}" \
  -H "X-Service-Secret: ${SERVICE_SECRET}" 2>&1) || true
  -H "Idempotency-Key: ${IDEMPOTENCY_KEY}" \
  -d '{
    "amountCents": 100,
    "currency": "usd",
    "provider": "stripe",
    "isTest": true
  }')

SESSION_ID=$(echo "${SESSION_RESPONSE}" | jq -r '.sessionId // .id // empty')
PAYMENT_INTENT_ID=$(echo "${SESSION_RESPONSE}" | jq -r '.paymentIntentId // .externalId // empty')

if [[ -z "${SESSION_ID}" ]]; then
  log "FAIL: Could not create payment session"
  log "Response: ${SESSION_RESPONSE}"
  exit 1
fi
log "OK: Payment session created (id=${SESSION_ID})"

# Phase 3: Simulate Stripe webhook
TIMESTAMP=$(date +%s)
WEBHOOK_PAYLOAD=$(cat <<PAYLOAD
{
  "id": "evt_synth_${TIMESTAMP}",
  "object": "event",
  "type": "checkout.session.completed",
  "data": {
    "object": {
      "id": "${PAYMENT_INTENT_ID:-cs_synth_${TIMESTAMP}}",
      "payment_status": "paid",
      "metadata": {
        "sessionId": "${SESSION_ID}",
        "isTest": "true"
      }
    }
  },
  "livemode": false
}
PAYLOAD
)

# Compute Stripe signature
SIGNED_PAYLOAD="${TIMESTAMP}.${WEBHOOK_PAYLOAD}"
SIGNATURE=$(printf '%s' "${SIGNED_PAYLOAD}" | openssl dgst -sha256 -hmac "${STRIPE_WEBHOOK_SECRET}" | sed 's/^.* //')
STRIPE_SIGNATURE="t=${TIMESTAMP},v1=${SIGNATURE}"

log "Sending simulated Stripe webhook..."
WEBHOOK_RESPONSE=$(curl -s -o /dev/null -w "%{http_code}" --max-time 10 \
  -X POST "${BASE_URL}/api/v1/payments/webhooks/stripe" \
  -H "X-Service-Secret: ${SERVICE_SECRET}" 2>&1) || true
  -H "Stripe-Signature: ${STRIPE_SIGNATURE}" \
  -d "${WEBHOOK_PAYLOAD}")

if [[ "${WEBHOOK_RESPONSE}" -lt 200 || "${WEBHOOK_RESPONSE}" -ge 300 ]]; then
  log "FAIL: Webhook returned HTTP ${WEBHOOK_RESPONSE}"
  exit 1
fi
log "OK: Webhook accepted (HTTP ${WEBHOOK_RESPONSE})"

# Phase 4: Poll payment status
log "Polling payment status..."
ELAPSED=0
FINAL_STATE=""

while [[ ${ELAPSED} -lt ${POLL_TIMEOUT} ]]; do
  STATUS_RESPONSE=$(curl -s --max-time 10 \
    -H "${AUTH_HEADER}" \
    "${BASE_URL}/api/v1/payments/sessions/${SESSION_ID}" 2>/dev/null || echo '{}')

  CURRENT_STATUS=$(echo "${STATUS_RESPONSE}" | jq -r '.status // "Unknown"')
  log "  Status: ${CURRENT_STATUS} (${ELAPSED}s elapsed)"

  case "${CURRENT_STATUS}" in
    Completed|completed|Paid|paid|Succeeded|succeeded)
      FINAL_STATE="success"
      break
      ;;
    Failed|failed|Cancelled|cancelled)
      FINAL_STATE="failed"
      break
      ;;
  esac

  sleep "${POLL_INTERVAL}"
  ELAPSED=$((ELAPSED + POLL_INTERVAL))
done

# Phase 5: Cleanup test data
log "Cleaning up test payment session..."
curl -s --max-time 10 \
  -X DELETE "${BASE_URL}/api/v1/payments/sessions/${SESSION_ID}?isTest=true" \
  -H "${AUTH_HEADER}" > /dev/null 2>&1 || true
log "OK: Cleanup attempted"

# Report
if [[ "${FINAL_STATE}" == "success" ]]; then
  log "OK: Payment webhook flow completed successfully in ${ELAPSED}s"
  exit 0
elif [[ "${FINAL_STATE}" == "failed" ]]; then
  log "FAIL: Payment ended in failure state: ${CURRENT_STATUS}"
  exit 1
else
  log "FAIL: Payment status poll timed out after ${POLL_TIMEOUT}s (last: ${CURRENT_STATUS})"
  exit 1
fi
