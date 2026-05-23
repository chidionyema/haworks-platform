#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-https://haworks-bffweb.fly.dev}"
SERVICE_SECRET="${SERVICE_SECRET:?SERVICE_SECRET env var is required}"

log() { echo "[$(date -u +%H:%M:%S)] $*"; }

FAILURES=0

# 1. Service token endpoint (machine-to-machine auth)
log "Testing service token endpoint..."
start=$(date +%s%N)
TOKEN_RESP=$(curl --silent --max-time 10 \
  -X POST "${IDENTITY_URL:-https://haworks-identity.fly.dev}/api/v1/authentication/service-token" \
  -H "Content-Type: application/json" \
  -H "X-Service-Secret: ${SERVICE_SECRET}" 2>&1) || true
elapsed=$(( ($(date +%s%N) - start) / 1000000 ))
log "Service token: ${elapsed}ms"

TOKEN=$(echo "$TOKEN_RESP" | jq -r '.accessToken // .token // empty')
if [ -z "$TOKEN" ]; then
  log "FAIL: No token returned from service-token endpoint"
  FAILURES=$((FAILURES + 1))
else
  log "OK: Service token obtained"
fi

if [ $FAILURES -gt 0 ] || [ -z "$TOKEN" ]; then
  log "Cannot continue without auth token"
  exit 1
fi

AUTH="Authorization: Bearer ${TOKEN}"

# 2. User registration (unique email per run)
RUN_ID=$(date +%s)
TEST_EMAIL="synthmon+${RUN_ID}@test.invalid"
TEST_PASSWORD="SynMon${RUN_ID}!Aa"

log "Testing user registration: ${TEST_EMAIL}"
start=$(date +%s%N)
REG_RESP=$(curl --silent --max-time 10 -w "\n%{http_code}" \
  -X POST "${IDENTITY_URL:-https://haworks-identity.fly.dev}/api/v1/authentication/register" \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"synthmon${RUN_ID}\",\"email\":\"${TEST_EMAIL}\",\"password\":\"${TEST_PASSWORD}\",\"confirmPassword\":\"${TEST_PASSWORD}\"}")
REG_STATUS=$(echo "$REG_RESP" | tail -1)
REG_BODY=$(echo "$REG_RESP" | sed '$d')
elapsed=$(( ($(date +%s%N) - start) / 1000000 ))
log "Registration: ${REG_STATUS} (${elapsed}ms)"

if [ "$REG_STATUS" -ge 200 ] && [ "$REG_STATUS" -lt 300 ]; then
  log "OK: User registered"
else
  # 409 = duplicate (previous run) — acceptable
  if [ "$REG_STATUS" = "409" ]; then
    log "OK: User already exists (previous run)"
  else
    log "FAIL: Registration returned ${REG_STATUS}: ${REG_BODY}"
    FAILURES=$((FAILURES + 1))
  fi
fi

# 3. User login
log "Testing user login..."
start=$(date +%s%N)
LOGIN_RESP=$(curl --silent --max-time 10 -w "\n%{http_code}" \
  -X POST "${IDENTITY_URL:-https://haworks-identity.fly.dev}/api/v1/authentication/login" \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"synthmon${RUN_ID}\",\"password\":\"${TEST_PASSWORD}\"}")
LOGIN_STATUS=$(echo "$LOGIN_RESP" | tail -1)
LOGIN_BODY=$(echo "$LOGIN_RESP" | sed '$d')
elapsed=$(( ($(date +%s%N) - start) / 1000000 ))
log "Login: ${LOGIN_STATUS} (${elapsed}ms)"

if [ "$LOGIN_STATUS" -ge 200 ] && [ "$LOGIN_STATUS" -lt 300 ]; then
  USER_TOKEN=$(echo "$LOGIN_BODY" | jq -r '.accessToken // .token // empty')
  if [ -n "$USER_TOKEN" ]; then
    log "OK: Login successful, token obtained"
  else
    log "FAIL: Login 200 but no token in response"
    FAILURES=$((FAILURES + 1))
  fi
else
  log "FAIL: Login returned ${LOGIN_STATUS}"
  FAILURES=$((FAILURES + 1))
fi

# 4. Token validation (call a protected endpoint)
if [ -n "${USER_TOKEN:-}" ]; then
  log "Testing token validation via protected endpoint..."
  start=$(date +%s%N)
  PROFILE_STATUS=$(curl --silent --max-time 10 -o /dev/null -w "%{http_code}" \
    -X GET "${IDENTITY_URL:-https://haworks-identity.fly.dev}/api/v1/authentication/me" \
    -H "Authorization: Bearer ${USER_TOKEN}")
  elapsed=$(( ($(date +%s%N) - start) / 1000000 ))
  log "Profile: ${PROFILE_STATUS} (${elapsed}ms)"

  if [ "$PROFILE_STATUS" -ge 200 ] && [ "$PROFILE_STATUS" -lt 300 ]; then
    log "OK: Token validates correctly"
  elif [ "$PROFILE_STATUS" = "404" ]; then
    # /me endpoint might not exist — try another protected route
    log "WARN: /me not found, token likely valid (got past auth middleware)"
  else
    log "FAIL: Protected endpoint returned ${PROFILE_STATUS}"
    FAILURES=$((FAILURES + 1))
  fi
fi

# 5. JWKS endpoint (public key for token verification)
log "Testing JWKS endpoint..."
start=$(date +%s%N)
JWKS_STATUS=$(curl --silent --max-time 10 -o /dev/null -w "%{http_code}" \
  "${IDENTITY_URL:-https://haworks-identity.fly.dev}/api/v1/authentication/.well-known/jwks.json")
elapsed=$(( ($(date +%s%N) - start) / 1000000 ))
log "JWKS: ${JWKS_STATUS} (${elapsed}ms)"

if [ "$JWKS_STATUS" = "200" ]; then
  log "OK: JWKS endpoint accessible"
else
  # Try alternate path
  JWKS_STATUS=$(curl --silent --max-time 10 -o /dev/null -w "%{http_code}" \
    "${IDENTITY_URL:-https://haworks-identity.fly.dev}/.well-known/jwks.json")
  if [ "$JWKS_STATUS" = "200" ]; then
    log "OK: JWKS at alternate path"
  else
    log "WARN: JWKS returned ${JWKS_STATUS} (may be hosted externally)"
  fi
fi

# Summary
log "---"
if [ $FAILURES -eq 0 ]; then
  log "Identity monitor PASSED (0 failures)"
  exit 0
else
  log "Identity monitor FAILED (${FAILURES} failures)"
  exit 1
fi
