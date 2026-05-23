#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-https://haworks-bffweb.fly.dev}"
SERVICE_SECRET="${SERVICE_SECRET:?SERVICE_SECRET is required}"
POLL_TIMEOUT=30
POLL_INTERVAL=3

log() { echo "[$(date -u +%H:%M:%S)] $*"; }

log "=== Notification Synthetic Monitor ==="
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

# Phase 2: Send test notification
IDEMPOTENCY_KEY="synth-notif-$(date -u +%Y%m%d%H%M%S)-$$"
log "Sending test notification..."

NOTIF_RESPONSE=$(curl -s --max-time 10 \
  -X POST "${NOTIFICATIONS_URL:-https://haworks-notifications.fly.dev}/api/v1/notifications" \
  -H "${AUTH_HEADER}" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: ${IDEMPOTENCY_KEY}" \
  -d '{
    "channel": "email",
    "recipientId": "synthetic-monitor",
    "templateId": "test-ping",
    "payload": {
      "subject": "Synthetic Monitor Ping",
      "message": "Automated health check"
    },
    "isTest": true
  }' 2>&1) || true

NOTIFICATION_ID=$(echo "${NOTIF_RESPONSE}" | jq -r '.notificationId // .id // empty')

if [[ -z "${NOTIFICATION_ID}" ]]; then
  log "FAIL: Could not create notification"
  log "Response: ${NOTIF_RESPONSE}"
  exit 1
fi
log "OK: Notification created (id=${NOTIFICATION_ID})"

# Phase 3: Poll for delivery status
log "Polling notification status (timeout=${POLL_TIMEOUT}s)..."
ELAPSED=0
FINAL_STATE=""

while [[ ${ELAPSED} -lt ${POLL_TIMEOUT} ]]; do
  STATUS_RESPONSE=$(curl -s --max-time 10 \
    -H "${AUTH_HEADER}" \
    "${NOTIFICATIONS_URL:-https://haworks-notifications.fly.dev}/api/v1/notifications/${NOTIFICATION_ID}" 2>/dev/null || echo '{}')

  CURRENT_STATUS=$(echo "${STATUS_RESPONSE}" | jq -r '.status // "Unknown"')
  log "  Status: ${CURRENT_STATUS} (${ELAPSED}s elapsed)"

  case "${CURRENT_STATUS}" in
    Sent|sent|Delivered|delivered)
      FINAL_STATE="success"
      break
      ;;
    Failed|failed|Bounced|bounced)
      FINAL_STATE="failed"
      break
      ;;
  esac

  sleep "${POLL_INTERVAL}"
  ELAPSED=$((ELAPSED + POLL_INTERVAL))
done

# Report
if [[ "${FINAL_STATE}" == "success" ]]; then
  log "OK: Notification delivered in ${ELAPSED}s"
  exit 0
elif [[ "${FINAL_STATE}" == "failed" ]]; then
  log "FAIL: Notification delivery failed (status: ${CURRENT_STATUS})"
  exit 1
else
  log "FAIL: Notification delivery timed out after ${POLL_TIMEOUT}s (last: ${CURRENT_STATUS})"
  exit 1
fi
