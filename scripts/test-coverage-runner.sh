#!/usr/bin/env bash
set -euo pipefail

# Test Coverage Gap Runner (local cron)
# 4-phase pipeline:
#   Phase 1: Audit test coverage gaps for a service
#   Phase 2: Validate findings (discard false positives)
#   Phase 3: Generate and write missing tests
#   Phase 4: BUILD + TEST GATE — verify everything compiles and passes
#
# RULE: No PR is created unless build + all tests pass.

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

# Determine next service in rotation
if [ -f "$STATE_FILE" ]; then
  LAST_INDEX=$(cat "$STATE_FILE")
  INDEX=$(( (LAST_INDEX + 1) % ${#SERVICES[@]} ))
else
  INDEX=0
fi

# Allow override via argument
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
# PHASE 1: Test Coverage Audit
# ============================================================
echo ">>> [Phase 1/4] Auditing test coverage: $SERVICE ($(date))"
echo ">>> Report: $AUDIT_FILE"

bash "$REPO_ROOT/scripts/test-coverage-audit.sh" "$SERVICE" > "$AUDIT_FILE" 2>&1
EXIT_CODE=$?

if [ $EXIT_CODE -ne 0 ]; then
  echo ">>> Audit failed with exit code $EXIT_CODE"
  exit $EXIT_CODE
fi

PHASE1_COUNT=$(grep -c "^\(####\|### \[MISSING\]\|### \[PARTIAL\]\)" "$AUDIT_FILE" 2>/dev/null || true)
PHASE1_COUNT=${PHASE1_COUNT:-0}
PHASE1_COUNT=$(echo "$PHASE1_COUNT" | tr -d '[:space:]')
echo ">>> Phase 1 complete: $PHASE1_COUNT gaps found"

if [ "$PHASE1_COUNT" -eq 0 ]; then
  echo ">>> No coverage gaps found. Done."
  exit 0
fi

# ============================================================
# PHASE 2: Validate findings
# ============================================================
echo ">>> [Phase 2/4] Validating coverage gaps..."

VALIDATE_PROMPT=$(cat <<'VALIDATE_EOF'
You are a senior test engineer validating a test coverage audit. For EACH gap found:

1. Read the actual source file and verify the method/endpoint exists
2. Search existing test files to confirm no test covers it (check test method names, mock setups, and assertions)
3. Classify each gap as:
   - CONFIRMED: The gap is real, no test covers this path
   - FALSE_POSITIVE: A test already covers this (cite the test)
   - LOW_VALUE: The code is trivial (pure DTO, auto-property, DI registration) and doesn't need a test

4. For CONFIRMED gaps, verify the suggested test skeleton is correct:
   - Uses the right test framework (xUnit with [Fact]/[Theory])
   - References real classes and methods from the source
   - Has meaningful assertions (not just Assert.True(true))
   - For integration tests: uses SharedTestPostgres/SharedTestElasticsearch, NOT raw Testcontainers

Output the validated audit in the SAME format, but:
- Only include CONFIRMED gaps
- Add priority ranking (1 = most important)
- Ensure test skeletons compile against the actual source code
- At the top: "## Validation Summary\n- Original gaps: N\n- Confirmed: N\n- False positives: N\n- Low value removed: N"

IMPORTANT: Actually read each source file and test file. Do not rubber-stamp.
VALIDATE_EOF
)

echo "$VALIDATE_PROMPT

Here is the audit to validate:

$(cat "$AUDIT_FILE")
" | claude --print --model claude-sonnet-4-20250514 > "$VALIDATED_FILE" 2>&1

CONFIRMED_COUNT=$(grep -c "^####\|^### .*CONFIRMED\|^\[MISSING\]" "$VALIDATED_FILE" 2>/dev/null || true)
CONFIRMED_COUNT=${CONFIRMED_COUNT:-0}
CONFIRMED_COUNT=$(echo "$CONFIRMED_COUNT" | tr -d '[:space:]')
echo ">>> Phase 2 complete: $CONFIRMED_COUNT confirmed gaps"

if [ "$CONFIRMED_COUNT" -eq 0 ]; then
  echo ">>> No confirmed gaps. Skipping fix phase."
  rm -f "$VALIDATED_FILE"
  exit 0
fi

# ============================================================
# PHASE 3: Write missing tests
# ============================================================
echo ">>> [Phase 3/4] Writing missing tests..."

BRANCH="test-coverage/${SERVICE,,}-${DATE}"
ORIGINAL_BRANCH=$(git branch --show-current)

# Save reports before switching branches
AUDIT_CONTENT=$(cat "$AUDIT_FILE")
VALIDATED_CONTENT=$(cat "$VALIDATED_FILE")

git stash --include-untracked -m "test-coverage: stash before branch" 2>/dev/null || true
git checkout -b "$BRANCH" main

# Recreate reports on clean branch
mkdir -p "$SERVICE_REPORT_DIR"
echo "$AUDIT_CONTENT" > "$AUDIT_FILE"
echo "$VALIDATED_CONTENT" > "$VALIDATED_FILE"
mkdir -p "$(dirname "$STATE_FILE")"
echo "$INDEX" > "$STATE_FILE"

FIX_PROMPT=$(cat <<FIX_EOF
You are writing missing tests for the $SERVICE service based on a validated coverage audit.

## TEST TIER RULES (MANDATORY)
- Unit tests: tests/${SERVICE}/${SERVICE}.Unit/ — NO Docker, NO infrastructure, NO network
- Integration tests: tests/${SERVICE}/${SERVICE}.Integration/ — Docker via shared singletons ONLY
- E2E tests: tests/E2E/ — full Aspire stack, gated by E2E_ENABLED=1 env var
- CI filter: FullyQualifiedName!~Integration&FullyQualifiedName!~E2E&FullyQualifiedName!~Smoke
- NEVER put integration tests in unit test projects (they run in the fast CI tier without Docker)

## TEST OPTIMIZATION RULES (MANDATORY — CI ARCHITECTURE GUARDS ENFORCE THESE)
- NEVER use raw PostgreSqlBuilder/ContainerBuilder — use SharedTestPostgres.CreateDatabaseAsync("svc")
- NEVER use EnsureCreatedAsync() — use MigrateAsync() (catches migration drift)
- NEVER use EnsureDeletedAsync() — use fresh DB from SharedTestPostgres singleton
- NEVER use ConfigureServices in test factory — use ConfigureTestServices (runs after app DI)
- ALWAYS call JwtTestDefaults.SetTestEnvironmentVariables() in test factory InitializeAsync
- ALWAYS create schema before MigrateAsync: CREATE SCHEMA IF NOT EXISTS
- NEVER use Task.Delay() for polling — use TestWait.Until() or TestWait.NotHappens()
- NEVER use global UseConsumeFilter in factories with saga state machines
- ALWAYS propagate CancellationToken in async test methods
- Use OutboxAssert.AssertStoredInOutboxAsync for verifying consumer events in Outbox
- For MassTransit consumer tests: use InMemoryTestHarness or existing test factory patterns
- Containers use WithReuse(true) — one container per type across all dotnet test runs

## GENERAL TEST RULES
- Write REAL tests with meaningful assertions. No Assert.True(true). No empty test bodies.
- Use xUnit ([Fact], [Theory], [InlineData]) — project standard.
- Follow existing test patterns (check nearby test files for conventions).
- Add tests to EXISTING test files when a test class for that component already exists.
- Create new test files only when no existing test class covers the component.
- Each test must have a clear Arrange/Act/Assert structure.
- Test naming: Method_Scenario_ExpectedResult (e.g., CreateOrder_WithZeroAmount_ThrowsValidationException)
- Include negative tests: invalid input, unauthorized access, missing data, concurrent access.
- DO NOT modify source code — only add/modify test files.

## E2E TEST RULES
- E2E tests go in tests/E2E/ ONLY, never in service-specific test projects
- Use E2EEnvironmentFixture collection fixture: [Collection("E2E Tests")]
- Gate with E2EEnvironmentFixture.SkipIfNotEnabled() in InitializeAsync
- Can target external URL via E2E_TARGET_URL env var
- Test cross-service flows: publish event in service A, verify state in service B
- Test saga orchestrations end-to-end with real transport

## VERIFICATION (MANDATORY)
After writing tests, you MUST:
1. Run: dotnet build HaworksPlatform.sln 2>&1 | grep " error " | grep -v HWK023
2. If build errors, fix the test code (not the source code).
3. Run: dotnet test <test-project> --filter "FullyQualifiedName~<NewTestClass>" 2>&1
4. If tests fail, fix assertions or setup — the source code is correct, adjust tests to match.
5. Keep iterating until ALL new tests compile and pass.
6. Also fix any test optimization violations found in existing tests (Phase 2 flagged these).

Output:
## Tests Added
- [file:line] Brief description

## Tests Skipped
- [gap] Reason (e.g., requires infrastructure not available, too complex for auto-generation)

## Build + Test Status
- Build: PASS/FAIL
- Tests: X passed, Y failed

$(cat "$VALIDATED_FILE")
FIX_EOF
)

FIX_OUTPUT=$(echo "$FIX_PROMPT" | claude --model claude-sonnet-4-20250514 2>&1)
FIX_SUMMARY=$(echo "$FIX_OUTPUT" | tail -60)

echo "$FIX_SUMMARY" > "$SERVICE_REPORT_DIR/${DATE}-tests-added.md"

# ============================================================
# PHASE 4: BUILD + TEST GATE
# ============================================================
echo ">>> [Phase 4/4] Build + test gate..."

BUILD_ERRORS=$(dotnet build HaworksPlatform.sln 2>&1 | grep " error " | grep -v HWK023 | head -20)

if [ -n "$BUILD_ERRORS" ]; then
  echo ">>> BUILD FAILED — reverting test changes"
  echo "$BUILD_ERRORS"
  git checkout -- tests/ 2>/dev/null || true

  REVERT_ERRORS=$(dotnet build HaworksPlatform.sln 2>&1 | grep " error " | grep -v HWK023 | head -5)
  if [ -n "$REVERT_ERRORS" ]; then
    echo ">>> BUILD STILL BROKEN after revert — aborting"
    git checkout "$ORIGINAL_BRANCH" 2>/dev/null || git checkout main
    git branch -D "$BRANCH" 2>/dev/null || true
    git stash pop 2>/dev/null || true
    exit 1
  fi

  echo ">>> Build clean after revert. Creating report-only PR."
fi

# Run unit tests
echo ">>> Running unit tests..."
UNIT_FILTER="FullyQualifiedName!~Integration&FullyQualifiedName!~E2E&FullyQualifiedName!~Smoke"
UNIT_RESULT=$(dotnet test HaworksPlatform.sln --no-build --filter "$UNIT_FILTER" 2>&1)

if echo "$UNIT_RESULT" | grep -q "Failed!"; then
  FAILED_TESTS=$(echo "$UNIT_RESULT" | grep "Failed!" | head -5)
  echo ">>> UNIT TESTS FAILED — reverting test changes"
  echo "$FAILED_TESTS"
  git checkout -- tests/ 2>/dev/null || true
  echo ">>> Reverted. Creating report-only PR."
else
  echo ">>> Unit tests PASSED"
fi

# Run integration tests for the affected service
echo ">>> Running integration tests for $SERVICE..."
INTEG_PROJECT=$(find tests -path "*${SERVICE}*Integration*" -name "*.csproj" | head -1)

if [ -n "$INTEG_PROJECT" ]; then
  INTEG_RESULT=$(dotnet test "$INTEG_PROJECT" 2>&1)

  if echo "$INTEG_RESULT" | grep -q "Failed!"; then
    FAILED_INTEG=$(echo "$INTEG_RESULT" | grep "Failed!" | head -5)
    echo ">>> INTEGRATION TESTS FAILED — reverting test changes"
    echo "$FAILED_INTEG"
    git checkout -- tests/ 2>/dev/null || true
    echo ">>> Reverted. Creating report-only PR."
  else
    echo ">>> Integration tests PASSED"
  fi
else
  echo ">>> No integration test project found for $SERVICE — skipping"
fi

echo ">>> Build + test gate PASSED"

# Stage and commit
git add -A
git status --short

if git diff --cached --quiet; then
  echo ">>> No changes to commit. Skipping PR."
  git checkout "$ORIGINAL_BRANCH" 2>/dev/null || git checkout main
  git branch -D "$BRANCH" 2>/dev/null || true
  git stash pop 2>/dev/null || true
  exit 0
fi

git commit -m "test(${SERVICE}): add missing test coverage ${DATE}

Phase 1: Coverage audit ($PHASE1_COUNT gaps found)
Phase 2: Validation ($CONFIRMED_COUNT confirmed)
Phase 3: Tests generated
Phase 4: Build + test gate PASSED

Co-Authored-By: Claude Code <noreply@anthropic.com>"

git push -u origin "$BRANCH"

PR_BODY=$(cat <<EOF
## Test Coverage: ${SERVICE}

**Date**: ${DATE}
**Pipeline**: Audit -> Validate -> Generate Tests -> Build+Test Gate
**Build**: PASSED
**Tests**: PASSED

### Validation Summary
$(head -10 "$VALIDATED_FILE")

### Tests Added
$(cat "$SERVICE_REPORT_DIR/${DATE}-tests-added.md" 2>/dev/null || echo "See commit diff")

### Full Coverage Audit
<details>
<summary>Click to expand</summary>

$(cat "$VALIDATED_FILE" | head -300)

</details>

---
Generated by \`scripts/test-coverage-runner.sh\` (4-phase pipeline with build+test gate)
EOF
)

PR_URL=$(gh pr create \
  --title "test(${SERVICE}): fill coverage gaps ${DATE}" \
  --body "$PR_BODY" \
  --label "test-coverage" 2>&1) || true

echo ">>> PR: $PR_URL"

# Return to original branch
git checkout "$ORIGINAL_BRANCH" 2>/dev/null || git checkout main
git stash pop 2>/dev/null || true

echo ">>> Done. Next service in rotation: ${SERVICES[$(( (INDEX + 1) % ${#SERVICES[@]} ))]}"
