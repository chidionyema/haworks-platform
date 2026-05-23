#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-https://haworks-bffweb.fly.dev}"
SERVICE_SECRET="${SERVICE_SECRET:?SERVICE_SECRET is required}"
POLL_TIMEOUT=60
POLL_INTERVAL=5

log() { echo "[$(date -u +%H:%M:%S)] $*"; }

log "=== GDPR Synthetic Monitor ==="
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

# Phase 2: Initiate a test erasure request
IDEMPOTENCY_KEY="synth-gdpr-$(date -u +%Y%m%d%H%M%S)-$$"
TEST_USER_ID="synth-gdpr-user-$(date -u +%Y%m%d%H%M%S)-$$"
log "Initiating GDPR erasure request for test user: ${TEST_USER_ID}..."

ERASURE_RESPONSE=$(curl -s --max-time 10 \
  -X POST "${BASE_URL}/api/v1/privacy/requests" \
  -H "${AUTH_HEADER}" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: ${IDEMPOTENCY_KEY}" \
  -d "{
    \"userId\": \"${TEST_USER_ID}\",
    \"requestType\": \"erasure\",
    \"reason\": \"Synthetic monitor GDPR test\",
    \"isTest\": true
  }" 2>&1) || true

REQUEST_ID=$(echo "${ERASURE_RESPONSE}" | jq -r '.requestId // .id // empty')
if [[ -z "${REQUEST_ID}" ]]; then
  log "FAIL: Could not extract requestId from response"
  log "Response: ${ERASURE_RESPONSE}"
  exit 1
fi
log "OK: GDPR erasure request created (id=${REQUEST_ID})"

# Phase 3: Poll until terminal state
log "Polling GDPR request state (timeout=${POLL_TIMEOUT}s, interval=${POLL_INTERVAL}s)..."
ELAPSED=0
FINAL_STATE=""

while [[ ${ELAPSED} -lt ${POLL_TIMEOUT} ]]; do
  STATUS_RESPONSE=$(curl -s --max-time 10 \
    -H "${AUTH_HEADER}" \
    "${BASE_URL}/api/v1/privacy/requests/${REQUEST_ID}" 2>/dev/null || echo '{}')

  CURRENT_STATE=$(echo "${STATUS_RESPONSE}" | jq -r '.state // .status // "Unknown"')
  log "  State: ${CURRENT_STATE} (${ELAPSED}s elapsed)"

  case "${CURRENT_STATE}" in
    Completed|completed|Done|done)
      FINAL_STATE="completed"
      break
      ;;
    Failed|failed|Error|error)
      FINAL_STATE="failed_terminal"
      break
      ;;
    PartiallyCompleted|partially_completed)
      FINAL_STATE="partial"
      break
      ;;
  esac

  sleep "${POLL_INTERVAL}"
  ELAPSED=$((ELAPSED + POLL_INTERVAL))
done

# Phase 4: Report
case "${FINAL_STATE}" in
  completed)
    log "OK: GDPR erasure completed in ${ELAPSED}s"
    exit 0
    ;;
  partial)
    log "OK: GDPR erasure partially completed in ${ELAPSED}s (some services responded)"
    exit 0
    ;;
  failed_terminal)
    log "OK: GDPR erasure reached terminal failure in ${ELAPSED}s (not stuck)"
    log "NOTE: Terminal failure is acceptable for synthetic test — saga is not stuck"
    exit 0
    ;;
  *)
    log "FAIL: GDPR erasure timed out after ${POLL_TIMEOUT}s (last state: ${CURRENT_STATE})"
    exit 1
    ;;
esac
