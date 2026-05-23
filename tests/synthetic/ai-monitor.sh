#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-https://haworks-bffweb.fly.dev}"
SERVICE_SECRET="${SERVICE_SECRET:?SERVICE_SECRET is required}"
MAX_RESPONSE_TIME=15

log() { echo "[$(date -u +%H:%M:%S)] $*"; }

check_response_time() {
  local label="$1"
  local elapsed="$2"
  if (( $(echo "${elapsed} > ${MAX_RESPONSE_TIME}" | bc -l) )); then
    log "FAIL: ${label} took ${elapsed}s (max ${MAX_RESPONSE_TIME}s)"
    exit 1
  fi
  log "OK: ${label} responded in ${elapsed}s"
}

log "=== AI Synthetic Monitor ==="
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
FAILURES=0

# Phase 2: AI Search
log "Testing AI search..."
START_TIME=$(date +%s%N)
SEARCH_RESPONSE=$(curl -s --max-time "${MAX_RESPONSE_TIME}" \
  -X POST "${BASE_URL}/api/v1/ai/search" \
  -H "${AUTH_HEADER}" \
  -H "Content-Type: application/json" \
  -d '{"query": "test product", "limit": 5}' 2>&1) || {
  log "FAIL: AI search returned error"
  log "Response: ${SEARCH_RESPONSE}"
  FAILURES=$((FAILURES + 1))
}
END_TIME=$(date +%s%N)
ELAPSED=$(echo "scale=2; (${END_TIME} - ${START_TIME}) / 1000000000" | bc -l)

if [[ ${FAILURES} -eq 0 ]]; then
  check_response_time "AI search" "${ELAPSED}"
fi

# Phase 3: AI Chat
SESSION_ID="synth-chat-$(date -u +%Y%m%d%H%M%S)-$$"
log "Testing AI chat (session: ${SESSION_ID})..."
START_TIME=$(date +%s%N)
CHAT_RESPONSE=$(curl -s --max-time "${MAX_RESPONSE_TIME}" \
  -X POST "${BASE_URL}/api/v1/ai/chat/message" \
  -H "${AUTH_HEADER}" \
  -H "Content-Type: application/json" \
  -d "{\"sessionId\": \"${SESSION_ID}\", \"message\": \"What products are available?\", \"isTest\": true}" 2>&1) || {
  log "FAIL: AI chat returned error"
  log "Response: ${CHAT_RESPONSE}"
  FAILURES=$((FAILURES + 1))
}
END_TIME=$(date +%s%N)
ELAPSED=$(echo "scale=2; (${END_TIME} - ${START_TIME}) / 1000000000" | bc -l)

if [[ ${FAILURES} -eq 0 ]]; then
  check_response_time "AI chat" "${ELAPSED}"
fi

# Phase 4: AI Recommendations
log "Testing AI recommendations..."
START_TIME=$(date +%s%N)
RECO_RESPONSE=$(curl -s --max-time "${MAX_RESPONSE_TIME}" \
  -H "${AUTH_HEADER}" \
  "${BASE_URL}/api/v1/ai/recommendations/test-user-id" 2>&1) || {
  log "FAIL: AI recommendations returned error"
  log "Response: ${RECO_RESPONSE}"
  FAILURES=$((FAILURES + 1))
}
END_TIME=$(date +%s%N)
ELAPSED=$(echo "scale=2; (${END_TIME} - ${START_TIME}) / 1000000000" | bc -l)

if [[ ${FAILURES} -eq 0 ]]; then
  check_response_time "AI recommendations" "${ELAPSED}"
fi

# Phase 5: AI Content Generation
log "Testing AI content generation..."
START_TIME=$(date +%s%N)
CONTENT_RESPONSE=$(curl -s --max-time "${MAX_RESPONSE_TIME}" \
  -X POST "${BASE_URL}/api/v1/ai/content/generate" \
  -H "${AUTH_HEADER}" \
  -H "Content-Type: application/json" \
  -d '{"type": "product_description", "context": "A premium handcrafted item", "isTest": true}' 2>&1) || {
  log "FAIL: AI content generation returned error"
  log "Response: ${CONTENT_RESPONSE}"
  FAILURES=$((FAILURES + 1))
}
END_TIME=$(date +%s%N)
ELAPSED=$(echo "scale=2; (${END_TIME} - ${START_TIME}) / 1000000000" | bc -l)

if [[ ${FAILURES} -eq 0 ]]; then
  check_response_time "AI content generation" "${ELAPSED}"
fi

# Report
if [[ ${FAILURES} -gt 0 ]]; then
  log "FAIL: ${FAILURES} AI endpoint(s) failed"
  exit 1
fi

log "OK: All AI endpoints responded within ${MAX_RESPONSE_TIME}s"
exit 0
