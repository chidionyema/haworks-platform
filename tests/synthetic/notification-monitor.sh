#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-https://haworks-bffweb.fly.dev}"
NOTIFICATIONS_URL="${NOTIFICATIONS_URL:-https://haworks-notifications.fly.dev}"
SERVICE_SECRET="${SERVICE_SECRET:?SERVICE_SECRET is required}"
POLL_TIMEOUT=30
POLL_INTERVAL=3

log() { echo "[$(date -u +%H:%M:%S)] $*"; }

log "=== Notification Synthetic Monitor ==="
log "Target: ${NOTIFICATIONS_URL}"

# Phase 0: Warm up notifications service (may be cold/stopped)
log "Warming up notifications service..."
curl -s --max-time 60 "${NOTIFICATIONS_URL}/health/ready" > /dev/null 2>&1 || true

# Phase 1: Authenticate
log "Authenticating..."
AUTH_RESPONSE=$(curl -s --max-time 15 \
  -X POST "${IDENTITY_URL:-https://haworks-identity.fly.dev}/api/v1/authentication/service-token" \
  -H "X-Service-Secret: ${SERVICE_SECRET}" 2>&1) || true

TOKEN=$(echo "${AUTH_RESPONSE}" | jq -r '.token // .accessToken // empty' 2>/dev/null)
if [[ -z "${TOKEN}" ]]; then
  log "FAIL: Authentication failed"
  log "Response: ${AUTH_RESPONSE}"
  exit 1
fi
log "OK: Authenticated"

AUTH_HEADER="Authorization: Bearer ${TOKEN}"

# Phase 2: Send test notification (30s timeout for cold start)
IDEMPOTENCY_KEY="synth-notif-$(date -u +%Y%m%d%H%M%S)-$$"
log "Sending test notification..."

NOTIF_RESPONSE=$(curl -s --max-time 60 \
  -X POST "${NOTIFICATIONS_URL}/api/v1/notifications" \
  -H "${AUTH_HEADER}" \
  -H "Content-Type: application/json" \
  -H "X-Idempotency-Key: ${IDEMPOTENCY_KEY}" \
  -d '{
    "recipient": "synthetic-monitor@test.invalid",
    "channel": 0,
    "templateId": "test-ping",
    "priority": 0,
    "variables": {},
    "idempotencyKey": "'"${IDEMPOTENCY_KEY}"'"
  }' 2>&1) || true

NOTIFICATION_ID=$(echo "${NOTIF_RESPONSE}" | jq -r '.notificationId // .id // empty' 2>/dev/null)

if [[ -z "${NOTIFICATION_ID}" ]]; then
  # Check if empty response (timeout) vs error response
  if [[ -z "${NOTIF_RESPONSE}" ]]; then
    log "FAIL: Notifications service not responding (timeout)"
  else
    log "FAIL: Could not create notification"
    log "Response: ${NOTIF_RESPONSE}"
  fi
  exit 1
fi
log "OK: Notification created (id=${NOTIFICATION_ID})"

# Phase 3: Poll for delivery status
log "Polling notification status (timeout=${POLL_TIMEOUT}s)..."
ELAPSED=0

while [[ ${ELAPSED} -lt ${POLL_TIMEOUT} ]]; do
  STATUS_RESPONSE=$(curl -s --max-time 10 \
    -H "${AUTH_HEADER}" \
    "${NOTIFICATIONS_URL}/api/v1/notifications/${NOTIFICATION_ID}" 2>/dev/null || echo '{}')

  CURRENT_STATUS=$(echo "${STATUS_RESPONSE}" | jq -r '.status // "Unknown"' 2>/dev/null)
  log "  Status: ${CURRENT_STATUS} (${ELAPSED}s elapsed)"

  case "${CURRENT_STATUS}" in
    Sent|sent|Delivered|delivered|1|2|3)
      log "OK: Notification delivered in ${ELAPSED}s"
      exit 0
      ;;
    Failed|failed|4)
      log "FAIL: Notification delivery failed (status: ${CURRENT_STATUS})"
      exit 1
      ;;
  esac

  sleep "${POLL_INTERVAL}"
  ELAPSED=$((ELAPSED + POLL_INTERVAL))
done

log "FAIL: Notification delivery timed out after ${POLL_TIMEOUT}s (last: ${CURRENT_STATUS})"
exit 1
