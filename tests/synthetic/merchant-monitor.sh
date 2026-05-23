#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-https://haworks-bffweb.fly.dev}"
SERVICE_SECRET="${SERVICE_SECRET:?SERVICE_SECRET is required}"

log() { echo "[$(date -u +%H:%M:%S)] $*"; }

log "=== Merchant Synthetic Monitor ==="
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

# Phase 2: Create a test merchant with unique slug
UNIQUE_SLUG="synth-merchant-$(date -u +%Y%m%d%H%M%S)-$$"
log "Creating test merchant (slug: ${UNIQUE_SLUG})..."

START_TIME=$(date +%s%N)
CREATE_RESPONSE=$(curl -s --max-time 5 \
  -X POST "${BASE_URL}/api/v1/merchants" \
  -H "${AUTH_HEADER}" \
  -H "Content-Type: application/json" \
  -d "{
    \"name\": \"Synthetic Monitor Merchant\",
    \"slug\": \"${UNIQUE_SLUG}\",
    \"email\": \"synth-${UNIQUE_SLUG}@test.haworks.dev\",
    \"isTest\": true
  }")
END_TIME=$(date +%s%N)
ELAPSED=$(echo "scale=2; (${END_TIME} - ${START_TIME}) / 1000000000" | bc -l)

MERCHANT_ID=$(echo "${CREATE_RESPONSE}" | jq -r '.merchantId // .id // empty')
if [[ -z "${MERCHANT_ID}" ]]; then
  log "FAIL: Could not extract merchantId from response"
  log "Response: ${CREATE_RESPONSE}"
  exit 1
fi

if (( $(echo "${ELAPSED} > 5" | bc -l) )); then
  log "FAIL: Merchant creation took ${ELAPSED}s (max 5s)"
  exit 1
fi
log "OK: Merchant created in ${ELAPSED}s (id=${MERCHANT_ID})"

# Phase 3: Verify merchant via GET
log "Verifying merchant via GET..."
GET_RESPONSE=$(curl -s --max-time 10 \
  -H "${AUTH_HEADER}" \
  "${BASE_URL}/api/v1/merchants/${MERCHANT_ID}")

RETURNED_SLUG=$(echo "${GET_RESPONSE}" | jq -r '.slug // empty')
if [[ -z "${RETURNED_SLUG}" ]]; then
  log "FAIL: Could not extract slug from GET response"
  log "Response: ${GET_RESPONSE}"
  exit 1
fi

if [[ "${RETURNED_SLUG}" != "${UNIQUE_SLUG}" ]]; then
  log "FAIL: Slug mismatch — expected '${UNIQUE_SLUG}', got '${RETURNED_SLUG}'"
  exit 1
fi
log "OK: Merchant verified (slug=${RETURNED_SLUG})"

log "OK: Merchant monitor passed"
exit 0
