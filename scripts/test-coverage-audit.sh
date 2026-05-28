#!/usr/bin/env bash
set -euo pipefail

SERVICE="${1:?Usage: test-coverage-audit.sh <ServiceName>}"
SERVICE_DIR="src/${SERVICE}"

if [ ! -d "$SERVICE_DIR" ]; then
  echo "ERROR: Service directory not found: $SERVICE_DIR"
  exit 1
fi

echo "=== Test Coverage Audit: ${SERVICE} ($(date -u +%Y-%m-%dT%H:%M:%SZ)) ==="

SERVICE_NAME=$(basename "$SERVICE_DIR")

# Gather source files
SRC_FILES=$(find "$SERVICE_DIR" -name "*.cs" \
  ! -path "*/bin/*" \
  ! -path "*/obj/*" \
  ! -path "*/Migrations/*.Designer.cs" \
  ! -path "*ModelSnapshot.cs" \
  | sort)

# Gather all test files (unit, integration, E2E)
UNIT_FILES=$(find "tests" -path "*${SERVICE_NAME}*Unit*" -name "*.cs" \
  ! -path "*/bin/*" ! -path "*/obj/*" 2>/dev/null | sort || true)
INTEG_FILES=$(find "tests" -path "*${SERVICE_NAME}*Integration*" -name "*.cs" \
  ! -path "*/bin/*" ! -path "*/obj/*" 2>/dev/null | sort || true)
E2E_FILES=$(find "tests/E2E" -name "*.cs" \
  ! -path "*/bin/*" ! -path "*/obj/*" 2>/dev/null | sort || true)

# Gather shared test infrastructure for reference
TESTING_INFRA=$(find "src/BuildingBlocks.Testing" -name "*.cs" \
  ! -path "*/bin/*" ! -path "*/obj/*" 2>/dev/null | sort || true)

SRC_COUNT=$(echo "$SRC_FILES" | grep -c . || true)
UNIT_COUNT=$(echo "$UNIT_FILES" | grep -c . || true)
INTEG_COUNT=$(echo "$INTEG_FILES" | grep -c . || true)
E2E_COUNT=$(echo "$E2E_FILES" | grep -c . || true)

echo "Source files: $SRC_COUNT"
echo "Unit test files: $UNIT_COUNT"
echo "Integration test files: $INTEG_COUNT"
echo "E2E test files: $E2E_COUNT"
echo ""

PROMPT=$(cat <<'PROMPT_EOF'
You are a Principal Test Engineer performing a comprehensive test coverage audit of a .NET microservice.
Your job is to find GAPS — code paths that exist in source but have NO corresponding test coverage.

## Analysis Method

For each source file, identify:
1. **Public methods** — each should have at least one unit test
2. **API endpoints** — each should have integration test coverage
3. **Consumer handlers** — each should have both unit and integration tests
4. **Domain logic** — state transitions, validation, business rules need thorough unit tests
5. **Error paths** — exception handling, validation failures, authorization denials
6. **Edge cases** — null inputs, empty collections, boundary values, concurrent access

## What to Check

### Unit Test Gaps
- Commands/Queries (MediatR handlers) without corresponding test class
- Domain entities with business logic but no unit tests
- Validators without tests for both valid and invalid inputs
- Services with branching logic but no tests covering each branch

### Integration Test Gaps
- API endpoints without integration tests (especially POST/PUT/DELETE)
- Database operations without integration tests
- Consumer message handling without integration tests
- Saga state machines without integration tests for each state transition

### E2E Test Gaps
- Critical user flows not covered end-to-end (checkout, payment, refund, merchant onboarding)
- Cross-service communication paths (service A publishes event, service B consumes)
- Saga orchestrations that span multiple services
- Authentication/authorization flows (login -> token -> protected endpoint)

### Test Quality Issues
- Tests that always pass (Assert.True(true))
- Tests that mock everything (testing mocks, not real behavior)
- Missing negative tests (only happy path tested)
- Missing concurrency tests for thread-unsafe code
- Brittle tests (asserting on implementation details, not behavior)

### Test Optimization Violations (MUST FLAG)
- Raw Testcontainers (PostgreSqlBuilder, ContainerBuilder) instead of SharedTestPostgres singletons
- EnsureCreatedAsync() instead of MigrateAsync() (hides migration drift)
- EnsureDeletedAsync() (drops entire test DB)
- ConfigureServices instead of ConfigureTestServices
- Missing JwtTestDefaults.SetTestEnvironmentVariables() in factory InitializeAsync
- Missing schema creation before MigrateAsync
- Task.Delay() instead of TestWait.Until() polling
- Global UseConsumeFilter in factories with saga state machines
- Integration tests in unit test projects
- Unit tests requiring Docker/infrastructure

## Output Format

### Coverage Map
- [COVERED] Component — tested by TestClass.TestMethod
- [MISSING] Component — no test found, PRIORITY: HIGH/MEDIUM/LOW
- [PARTIAL] Component — happy path only, missing: [list of scenarios]

### Missing Tests (ordered by priority)
#### [PRIORITY]: Test description
- **For**: SourceFile.cs:method_name
- **Type**: Unit | Integration | E2E
- **Why**: What risk does the missing test expose?
- **Skeleton**: compilable xUnit test with Arrange/Act/Assert

### Test Optimization Violations
#### [SEVERITY]: Description
- **File**: path/to/TestFile.cs:line
- **Violation**: What rule is broken
- **Fix**: Exact code change needed

### E2E Coverage Gaps
- [MISSING] Flow description — services involved
- E2E tests MUST be in tests/E2E/, gated by E2E_ENABLED=1

### Summary
- Total public methods: N, covered: N (X%)
- API endpoints tested: N/M
- Consumer handlers tested: N/M
- E2E flows tested: N/M
- Test optimization violations: N
- Critical gaps: N

PROMPT_EOF
)

# Build source context
SRC_CONTEXT=""
while IFS= read -r f; do
  [ -z "$f" ] && continue
  LINES=$(wc -l < "$f")
  if [ "$LINES" -le 500 ]; then
    SRC_CONTEXT+="
--- $f ---
$(cat "$f")
"
  fi
done <<< "$SRC_FILES"

# Build test context (unit + integration + E2E)
TEST_CONTEXT=""
for TEST_SET in "$UNIT_FILES" "$INTEG_FILES" "$E2E_FILES"; do
  if [ -n "$TEST_SET" ]; then
    while IFS= read -r f; do
      [ -z "$f" ] && continue
      LINES=$(wc -l < "$f")
      if [ "$LINES" -le 500 ]; then
        TEST_CONTEXT+="
--- $f ---
$(cat "$f")
"
      fi
    done <<< "$TEST_SET"
  fi
done

# Build shared test infrastructure context
INFRA_CONTEXT=""
if [ -n "$TESTING_INFRA" ]; then
  while IFS= read -r f; do
    [ -z "$f" ] && continue
    LINES=$(wc -l < "$f")
    if [ "$LINES" -le 300 ]; then
      INFRA_CONTEXT+="
--- $f ---
$(cat "$f")
"
    fi
  done <<< "$TESTING_INFRA"
fi

echo "$PROMPT

Audit test coverage for: $SERVICE_DIR

== PLATFORM TEST OPTIMIZATION RULES ==
- MUST use SharedTestPostgres.CreateDatabaseAsync(\"svc\") — never raw Testcontainers
- MUST use MigrateAsync() not EnsureCreatedAsync() in test factories
- MUST use ConfigureTestServices not ConfigureServices
- MUST call JwtTestDefaults.SetTestEnvironmentVariables() in InitializeAsync
- MUST create schema before MigrateAsync: CREATE SCHEMA IF NOT EXISTS
- MUST use TestWait.Until() instead of Task.Delay() for polling
- Unit tests: tests/{Service}/{Service}.Unit/ — no Docker
- Integration tests: tests/{Service}/{Service}.Integration/ — Docker via singletons
- E2E tests: tests/E2E/ — full Aspire stack, gated by E2E_ENABLED=1
- Test naming: Method_Scenario_ExpectedResult

== SHARED TEST INFRASTRUCTURE ==
$INFRA_CONTEXT

== SOURCE FILES ==
$SRC_CONTEXT

== EXISTING TESTS ==
$TEST_CONTEXT" | claude --print --model claude-sonnet-4-20250514
