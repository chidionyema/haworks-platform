#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-https://haworks-bffweb.fly.dev}"
MAX_RESPONSE_TIME=5
FAILURES=0

log() { echo "[$(date -u +%H:%M:%S)] $*"; }

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

# Test 1: Search endpoint
measure "GET /api/v1/search?q=test" \
  "${BASE_URL}/api/v1/search?q=test"

# Test 2: Products listing
measure "GET /api/v1/products?skip=0&take=1" \
  "${BASE_URL}/api/v1/products?skip=0&take=1"

# Test 3: AI search
measure "POST /api/v1/ai/search" \
  -X POST \
  -H "Content-Type: application/json" \
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
