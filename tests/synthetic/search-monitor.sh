#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-https://haworks-bffweb.fly.dev}"
SERVICE_SECRET="${SERVICE_SECRET:-}"
MAX_RESPONSE_TIME=5
FAILURES=0

log() { echo "[$(date -u +%H:%M:%S)] $*"; }

# Get auth token if SERVICE_SECRET is available
AUTH_HEADER=""
if [ -n "${SERVICE_SECRET}" ]; then
  TOKEN_RESP=$(curl -s --max-time 10 -X POST \
    "${IDENTITY_URL:-https://haworks-identity.fly.dev}/api/v1/authentication/service-token" \
    -H "X-Service-Secret: ${SERVICE_SECRET}" 2>&1) || true
  TOKEN=$(echo "${TOKEN_RESP}" | jq -r '.accessToken // .token // empty' 2>/dev/null)
  if [ -n "${TOKEN}" ]; then
    AUTH_HEADER="Authorization: Bearer ${TOKEN}"
    log "OK: Auth token obtained"
  else
    log "WARN: Could not get auth token, proceeding without"
  fi
fi

measure() {
  local label="$1"; shift
  local start end duration http_code body tmp

  tmp=$(mktemp)
  start=$(date +%s)
  http_code=$(curl -s -o "${tmp}" -w "%{http_code}" --max-time 10 "$@") || {
    log "FAIL: ${label} — curl error (exit $?)"
    rm -f "${tmp}"
    FAILURES=$((FAILURES + 1))
    return
  }
  end=$(date +%s)
  duration=$((end - start))
  body=$(cat "${tmp}")
  rm -f "${tmp}"

  if [[ "${http_code}" -lt 200 || "${http_code}" -ge 300 ]]; then
    log "FAIL: ${label} — HTTP ${http_code}"
    log "  Body: ${body}"
    FAILURES=$((FAILURES + 1))
    return
  fi

  if [[ ${duration} -gt ${MAX_RESPONSE_TIME} ]]; then
    log "FAIL: ${label} — ${duration}s exceeds ${MAX_RESPONSE_TIME}s threshold"
    FAILURES=$((FAILURES + 1))
    return
  fi

  log "OK: ${label} — HTTP ${http_code} in ${duration}s"
}

log "=== Search Synthetic Monitor ==="
log "Target: ${BASE_URL}"

# Build auth args
AUTH_ARGS=()
if [ -n "${AUTH_HEADER}" ]; then
  AUTH_ARGS=(-H "${AUTH_HEADER}")
fi

# Test 1: Search endpoint
measure "GET /api/v1/search?q=test" \
  "${AUTH_ARGS[@]}" "${BASE_URL}/api/v1/search?q=test"

# Test 2: Products listing
measure "GET /api/v1/products?skip=0&take=1" \
  "${AUTH_ARGS[@]}" "${BASE_URL}/api/v1/products?skip=0&take=1"

# Test 3: AI search
measure "POST /api/v1/ai/search" \
  -X POST \
  -H "Content-Type: application/json" \
  "${AUTH_ARGS[@]}" \
  -d '{"query": "test", "maxResults": 5}' \
  "${BASE_URL}/api/v1/ai/search"

# Report
log "=== Results: $((3 - FAILURES))/3 passed ==="
if [[ ${FAILURES} -gt 0 ]]; then
  log "FAIL: ${FAILURES} check(s) failed"
  exit 1
fi
log "OK: All search checks passed"
exit 0
