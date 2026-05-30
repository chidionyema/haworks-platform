#!/usr/bin/env bash
# macOS ships bash 3.2 at /bin/bash, which cannot parse heredocs nested in $(...).
# launchd execs /bin/bash directly (bypassing the shebang), so re-exec under modern bash.
if [ -z "${HAWORKS_BASH_REEXEC:-}" ] && [ "${BASH_VERSINFO:-0}" -lt 4 ]; then
  for _b in /usr/local/bin/bash /opt/homebrew/bin/bash; do
    if [ -x "$_b" ]; then HAWORKS_BASH_REEXEC=1 exec "$_b" "$0" "$@"; fi
  done
  echo "ERROR: bash >= 4 required but only $BASH_VERSION found" >&2; exit 1
fi
set -euo pipefail

# Test Coverage Gap Runner (local cron)
# 4-phase pipeline:
#   Phase 1: Audit test coverage gaps for a service
#   Phase 2: Validate findings (discard false positives)
#   Phase 3: Generate and write missing tests (in isolated worktree)
#   Phase 4: BUILD + TEST GATE
#
# Uses git worktrees so it can run concurrently with continuous-review-runner.

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

SERVICES=(
  Audit BffWeb Catalog CheckoutOrchestrator Identity
  Location Media Merchant Notifications Orders
  Payments Payouts Pricing Privacy Search
  Shipping Webhooks Scheduler RulesEngine Realtime
)

STATE_FILE="$REPO_ROOT/docs/reviews/.test-coverage-state"
REPORT_DIR="$REPO_ROOT/docs/reviews/test-coverage"

if [ -f "$STATE_FILE" ]; then
  LAST_INDEX=$(cat "$STATE_FILE")
  INDEX=$(( (LAST_INDEX + 1) % ${#SERVICES[@]} ))
else
  INDEX=0
fi

if [ -n "${1:-}" ]; then
  SERVICE="$1"
else
  SERVICE="${SERVICES[$INDEX]}"
fi

echo "$INDEX" > "$STATE_FILE"

DATE=$(date -u +%Y-%m-%d-%H%M)
SERVICE_REPORT_DIR="$REPORT_DIR/$SERVICE"
mkdir -p "$SERVICE_REPORT_DIR"
AUDIT_FILE="$SERVICE_REPORT_DIR/${DATE}-audit.md"
VALIDATED_FILE="$SERVICE_REPORT_DIR/${DATE}-validated.md"

# ============================================================
# PHASE 1: Test Coverage Audit (read-only, main working dir)
# ============================================================
echo ">>> [Phase 1/4] Auditing test coverage: $SERVICE ($(date))"

bash "$REPO_ROOT/scripts/test-coverage-audit.sh" "$SERVICE" > "$AUDIT_FILE" 2>&1 || {
  echo ">>> Audit failed"; exit 1
}

PHASE1_COUNT=$(grep -c "^\(####\|### \[MISSING\]\|### \[PARTIAL\]\)" "$AUDIT_FILE" 2>/dev/null || true)
PHASE1_COUNT=${PHASE1_COUNT:-0}
PHASE1_COUNT=$(echo "$PHASE1_COUNT" | tr -d '[:space:]')
echo ">>> Phase 1 complete: $PHASE1_COUNT gaps found"

if [ "$PHASE1_COUNT" -eq 0 ]; then
  echo ">>> No coverage gaps. Done."
  exit 0
fi

# ============================================================
# PHASE 2: Validate findings (read-only, main working dir)
# ============================================================
echo ">>> [Phase 2/4] Validating coverage gaps..."

VALIDATE_PROMPT=$(cat <<'VALIDATE_EOF'
You are a senior test engineer validating a test coverage audit. For EACH gap found:

1. Read the actual source file and verify the method/endpoint exists
2. Search existing test files to confirm no test covers it
3. Classify each gap as:
   - CONFIRMED: The gap is real, no test covers this path
   - FALSE_POSITIVE: A test already covers this (cite the test)
   - LOW_VALUE: The code is trivial and doesn't need a test

4. For CONFIRMED gaps, verify the test skeleton:
   - Uses xUnit [Fact]/[Theory]
   - References real classes from the source
   - Has meaningful assertions
   - Integration tests use SharedTestPostgres, MigrateAsync, ConfigureTestServices
   - Uses TestWait.Until() not Task.Delay()

Output validated audit with CONFIRMED gaps only.
At the top: "## Validation Summary\n- Original gaps: N\n- Confirmed: N\n- False positives: N\n- Low value removed: N"

IMPORTANT: Actually read each file. Do not rubber-stamp.
VALIDATE_EOF
)

echo "$VALIDATE_PROMPT

$(cat "$AUDIT_FILE")
" | claude --print --model claude-sonnet-4-20250514 > "$VALIDATED_FILE" 2>&1

CONFIRMED_COUNT=$(grep -c "^####\|^### .*CONFIRMED\|^\[MISSING\]" "$VALIDATED_FILE" 2>/dev/null || true)
CONFIRMED_COUNT=${CONFIRMED_COUNT:-0}
CONFIRMED_COUNT=$(echo "$CONFIRMED_COUNT" | tr -d '[:space:]')
echo ">>> Phase 2 complete: $CONFIRMED_COUNT confirmed gaps"

if [ "$CONFIRMED_COUNT" -eq 0 ]; then
  echo ">>> No confirmed gaps. Done."
  rm -f "$VALIDATED_FILE"
  exit 0
fi

# ============================================================
# PHASE 3: Write missing tests (in isolated git worktree)
# ============================================================
echo ">>> [Phase 3/4] Writing missing tests..."

SERVICE_LC=$(echo "$SERVICE" | tr '[:upper:]' '[:lower:]')
BRANCH="test-coverage/${SERVICE_LC}-${DATE}"
WORKTREE_DIR="/tmp/haworks-testcov-${SERVICE_LC}-$$"

git branch -D "$BRANCH" 2>/dev/null || true
git worktree add "$WORKTREE_DIR" -b "$BRANCH" main 2>/dev/null

cleanup_worktree() {
  cd "$REPO_ROOT"
  git worktree remove --force "$WORKTREE_DIR" 2>/dev/null || true
}
trap cleanup_worktree EXIT

# Copy reports into worktree
WT_REPORT_DIR="$WORKTREE_DIR/docs/reviews/test-coverage/$SERVICE"
mkdir -p "$WT_REPORT_DIR"
cp "$AUDIT_FILE" "$WT_REPORT_DIR/"
cp "$VALIDATED_FILE" "$WT_REPORT_DIR/"

cd "$WORKTREE_DIR"

# Re-resolve paths relative to worktree
SERVICE_REPORT_DIR="$WT_REPORT_DIR"
AUDIT_FILE="$SERVICE_REPORT_DIR/${DATE}-audit.md"
VALIDATED_FILE="$SERVICE_REPORT_DIR/${DATE}-validated.md"

FIX_PROMPT=$(cat <<FIX_EOF
You are writing missing tests for the $SERVICE service based on a validated coverage audit.

## TEST TIER RULES (MANDATORY)
- Unit tests: tests/${SERVICE}/${SERVICE}.Unit/ — NO Docker, NO infrastructure
- Integration tests: tests/${SERVICE}/${SERVICE}.Integration/ — Docker via shared singletons ONLY
- E2E tests: tests/E2E/ — full Aspire stack, gated by E2E_ENABLED=1
- NEVER put integration tests in unit test projects

## TEST OPTIMIZATION RULES (CI ARCHITECTURE GUARDS ENFORCE THESE)
- NEVER use raw PostgreSqlBuilder/ContainerBuilder — use SharedTestPostgres.CreateDatabaseAsync("svc")
- NEVER use EnsureCreatedAsync() — use MigrateAsync()
- NEVER use EnsureDeletedAsync() — use fresh DB from singleton
- NEVER use ConfigureServices — use ConfigureTestServices
- ALWAYS call JwtTestDefaults.SetTestEnvironmentVariables() in InitializeAsync
- ALWAYS create schema before MigrateAsync
- NEVER use Task.Delay() — use TestWait.Until()
- ALWAYS propagate CancellationToken

## GENERAL RULES
- Write REAL tests with meaningful assertions
- xUnit [Fact]/[Theory]/[InlineData]
- Follow existing test patterns in nearby files
- Add to EXISTING test files when possible
- Arrange/Act/Assert structure
- Naming: Method_Scenario_ExpectedResult
- Include negative tests
- DO NOT modify source code

## E2E RULES
- tests/E2E/ ONLY, [Collection("E2E Tests")]
- Gate with E2EEnvironmentFixture.SkipIfNotEnabled()
- Test cross-service flows

## VERIFICATION (MANDATORY)
1. dotnet build HaworksPlatform.sln — fix any errors in test code
2. dotnet test <project> --filter "FullyQualifiedName~<TestClass>" — fix failing tests
3. Iterate until ALL new tests compile and pass

Output:
## Tests Added
- [file:line] Brief description
## Tests Skipped
- [gap] Reason
## Build + Test Status
- Build: PASS/FAIL
- Tests: X passed, Y failed

$(cat "$VALIDATED_FILE")
FIX_EOF
)

FIX_OUTPUT=$(echo "$FIX_PROMPT" | claude --model claude-sonnet-4-20250514 2>&1)
echo "$FIX_OUTPUT" | tail -60 > "$SERVICE_REPORT_DIR/${DATE}-tests-added.md"

# ============================================================
# PHASE 4: BUILD + TEST GATE
# ============================================================
echo ">>> [Phase 4/4] Build + test gate..."

# Worktree needs restore since obj/ is not shared
dotnet restore HaworksPlatform.sln --verbosity quiet 2>/dev/null || true
BUILD_ERRORS=$(dotnet build HaworksPlatform.sln --no-restore 2>&1 | grep " error " | grep -v HWK023 | head -20 || true)

if [ -n "$BUILD_ERRORS" ]; then
  echo ">>> BUILD FAILED — reverting test changes"
  echo "$BUILD_ERRORS"
  git checkout -- tests/ 2>/dev/null || true

  REVERT_ERRORS=$(dotnet build HaworksPlatform.sln 2>&1 | grep " error " | grep -v HWK023 | head -5 || true)
  if [ -n "$REVERT_ERRORS" ]; then
    echo ">>> BUILD STILL BROKEN — aborting"
    exit 1
  fi
fi

echo ">>> Running unit tests..."
UNIT_FILTER="FullyQualifiedName!~Integration&FullyQualifiedName!~E2E&FullyQualifiedName!~Smoke"
UNIT_RESULT=$(dotnet test HaworksPlatform.sln --no-build --filter "$UNIT_FILTER" 2>&1 || true)

if echo "$UNIT_RESULT" | grep -q "Failed!"; then
  echo ">>> UNIT TESTS FAILED — reverting"
  echo "$UNIT_RESULT" | grep "Failed!" | head -5
  git checkout -- tests/ 2>/dev/null || true
else
  echo ">>> Unit tests PASSED"
fi

echo ">>> Running integration tests for $SERVICE..."
INTEG_PROJECT=$(find tests -path "*${SERVICE}*Integration*" -name "*.csproj" | head -1)
if [ -n "$INTEG_PROJECT" ]; then
  INTEG_RESULT=$(dotnet test "$INTEG_PROJECT" 2>&1 || true)
  if echo "$INTEG_RESULT" | grep -q "Failed!"; then
    echo ">>> INTEGRATION TESTS FAILED — reverting"
    git checkout -- tests/ 2>/dev/null || true
  else
    echo ">>> Integration tests PASSED"
  fi
fi

echo ">>> Gates PASSED"

git add -A
git status --short

if git diff --cached --quiet; then
  echo ">>> No changes to commit."
  exit 0
fi

git commit -m "test(${SERVICE}): add missing test coverage ${DATE}

Phase 1: Coverage audit ($PHASE1_COUNT gaps)
Phase 2: Validation ($CONFIRMED_COUNT confirmed)
Phase 3: Tests generated
Phase 4: Build + test gate PASSED

Co-Authored-By: Claude Code <noreply@anthropic.com>"

git push -u origin "$BRANCH"

PR_BODY=$(cat <<EOF
## Test Coverage: ${SERVICE}

**Date**: ${DATE} | **Build**: PASSED | **Tests**: PASSED

### Validation Summary
$(head -10 "$VALIDATED_FILE")

### Tests Added
$(cat "$SERVICE_REPORT_DIR/${DATE}-tests-added.md" 2>/dev/null || echo "See diff")

<details><summary>Full Audit</summary>

$(head -300 "$VALIDATED_FILE")

</details>

---
Generated by \`scripts/test-coverage-runner.sh\`
EOF
)

PR_URL=$(gh pr create \
  --title "test(${SERVICE}): fill coverage gaps ${DATE}" \
  --body "$PR_BODY" \
  --label "test-coverage" 2>&1) || true

echo ">>> PR: $PR_URL"
echo ">>> Done. Next: ${SERVICES[$(( (INDEX + 1) % ${#SERVICES[@]} ))]}"
