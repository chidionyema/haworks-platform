#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-https://haworks-bffweb.fly.dev}"
SERVICE_SECRET="${SERVICE_SECRET:?SERVICE_SECRET is required}"

log() { echo "[$(date -u +%H:%M:%S)] $*"; }

log "=== Audit Synthetic Monitor ==="
log "Target: ${BASE_URL}"

# Phase 1: Authenticate
log "Authenticating via service token..."
AUTH_RESPONSE=$(curl -s --fail-with-body --max-time 10 \
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

# Phase 2: Query audit entries from the last 5 minutes
NOW_UTC=$(date -u +%Y-%m-%dT%H:%M:%SZ)
FIVE_MIN_AGO=$(date -u -v-5M +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || date -u -d '5 minutes ago' +%Y-%m-%dT%H:%M:%SZ)

log "Querying audit entries from ${FIVE_MIN_AGO} to ${NOW_UTC}..."

AUDIT_RESPONSE=$(curl -s --fail-with-body --max-time 10 \
  -H "${AUTH_HEADER}" \
  "${BASE_URL}/api/v1/audit?from=${FIVE_MIN_AGO}&to=${NOW_UTC}")

# Phase 3: Validate response
ENTRY_COUNT=$(echo "${AUDIT_RESPONSE}" | jq -r '.totalCount // (.entries | length) // (.items | length) // 0')

if [[ "${ENTRY_COUNT}" == "null" || "${ENTRY_COUNT}" == "" ]]; then
  log "FAIL: Could not parse audit response"
  log "Response: ${AUDIT_RESPONSE}"
  exit 1
fi

log "Audit entries in last 5 minutes: ${ENTRY_COUNT}"

if [[ "${ENTRY_COUNT}" -eq 0 ]]; then
  log "FAIL: No audit entries found in last 5 minutes — audit pipeline may be broken"
  log "Response: ${AUDIT_RESPONSE}"
  exit 1
fi

log "OK: Audit pipeline healthy (${ENTRY_COUNT} entries in last 5 minutes)"
exit 0
