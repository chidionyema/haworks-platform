#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-https://haworks-bffweb.fly.dev}"

# Services with their Fly.io internal hostnames and criticality
declare -A SERVICES=(
  [identity]="haworks-identity.fly.dev"
  [catalog]="haworks-catalog.fly.dev"
  [orders]="haworks-orders.fly.dev"
  [payments]="haworks-payments.fly.dev"
  [checkout]="haworks-checkout.fly.dev"
  [search]="haworks-search.fly.dev"
  [notifications]="haworks-notifications.fly.dev"
  [payouts]="haworks-payouts.fly.dev"
  [audit]="haworks-audit.fly.dev"
  [scheduler]="haworks-scheduler.fly.dev"
  [bffweb]="haworks-bffweb.fly.dev"
  [ai]="haworks-ai.fly.dev"
)

CRITICAL_SERVICES="identity catalog orders payments checkout bffweb"

log() { echo "[$(date -u +%H:%M:%S)] $*"; }

HEALTHY=0
UNHEALTHY=0
CRITICAL_DOWN=0
RESULTS=""

log "=== Health Synthetic Monitor ==="
log "Target: ${BASE_URL}"
log "Checking ${#SERVICES[@]} services..."

for SERVICE in "${!SERVICES[@]}"; do
  HOST="${SERVICES[$SERVICE]}"
  HEALTH_URL="https://${HOST}/health/ready"

  HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" --max-time 10 "${HEALTH_URL}" 2>/dev/null || echo "000")

  if [[ "${HTTP_CODE}" -ge 200 && "${HTTP_CODE}" -lt 300 ]]; then
    log "OK: ${SERVICE} — healthy (HTTP ${HTTP_CODE})"
    HEALTHY=$((HEALTHY + 1))
    RESULTS="${RESULTS}  ${SERVICE}: UP\n"
  else
    log "FAIL: ${SERVICE} — unhealthy (HTTP ${HTTP_CODE})"
    UNHEALTHY=$((UNHEALTHY + 1))
    RESULTS="${RESULTS}  ${SERVICE}: DOWN (HTTP ${HTTP_CODE})\n"

    if echo "${CRITICAL_SERVICES}" | grep -qw "${SERVICE}"; then
      CRITICAL_DOWN=$((CRITICAL_DOWN + 1))
    fi
  fi
done

# Also check BFF health via the main URL
BFF_CODE=$(curl -s -o /dev/null -w "%{http_code}" --max-time 10 "${BASE_URL}/health/ready" 2>/dev/null || echo "000")
if [[ "${BFF_CODE}" -ge 200 && "${BFF_CODE}" -lt 300 ]]; then
  log "OK: bff-gateway — healthy (HTTP ${BFF_CODE})"
else
  log "FAIL: bff-gateway — unhealthy (HTTP ${BFF_CODE})"
  CRITICAL_DOWN=$((CRITICAL_DOWN + 1))
fi

# Report
TOTAL=${#SERVICES[@]}
log "=== Summary: ${HEALTHY}/${TOTAL} healthy, ${UNHEALTHY}/${TOTAL} unhealthy ==="
printf "${RESULTS}"

if [[ ${CRITICAL_DOWN} -gt 0 ]]; then
  log "FAIL: ${CRITICAL_DOWN} critical service(s) are down"
  exit 1
fi

if [[ ${UNHEALTHY} -gt 0 ]]; then
  log "WARN: ${UNHEALTHY} non-critical service(s) are down"
  exit 0
fi

log "OK: All services healthy"
exit 0
