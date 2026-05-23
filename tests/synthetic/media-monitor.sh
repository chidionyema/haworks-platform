#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-https://haworks-bffweb.fly.dev}"
SERVICE_SECRET="${SERVICE_SECRET:?SERVICE_SECRET is required}"

log() { echo "[$(date -u +%H:%M:%S)] $*"; }

log "=== Media Synthetic Monitor ==="
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

# Phase 2: Initiate an upload to get a presigned URL
log "Initiating media upload..."

START_TIME=$(date +%s%N)
UPLOAD_RESPONSE=$(curl -s --max-time 5 \
  -X POST "${BASE_URL}/api/v1/media/upload/initiate" \
  -H "${AUTH_HEADER}" \
  -H "Content-Type: application/json" \
  -d '{
    "fileName": "synth-monitor-test.png",
    "contentType": "image/png",
    "fileSizeBytes": 1024,
    "isTest": true
  }' 2>&1) || true
END_TIME=$(date +%s%N)
ELAPSED=$(echo "scale=2; (${END_TIME} - ${START_TIME}) / 1000000000" | bc -l)

if (( $(echo "${ELAPSED} > 5" | bc -l) )); then
  log "FAIL: Upload initiation took ${ELAPSED}s (max 5s)"
  exit 1
fi

# Phase 3: Verify presigned URL is returned
PRESIGNED_URL=$(echo "${UPLOAD_RESPONSE}" | jq -r '.presignedUrl // .uploadUrl // .url // empty')
UPLOAD_ID=$(echo "${UPLOAD_RESPONSE}" | jq -r '.uploadId // .id // empty')

if [[ -z "${PRESIGNED_URL}" ]]; then
  log "FAIL: No presigned URL returned from upload initiation"
  log "Response: ${UPLOAD_RESPONSE}"
  exit 1
fi

if [[ -z "${UPLOAD_ID}" ]]; then
  log "WARN: No uploadId returned (presigned URL was returned, continuing)"
fi

log "OK: Presigned URL received in ${ELAPSED}s"
log "OK: Upload ID: ${UPLOAD_ID:-N/A}"
log "OK: URL prefix: ${PRESIGNED_URL:0:60}..."

log "OK: Media upload initiation flow verified"
exit 0
