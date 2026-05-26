#!/usr/bin/env bash
set -euo pipefail

SERVICE="${1:?Usage: continuous-review.sh <ServiceName>}"
SERVICE_DIR="src/${SERVICE}"

if [ ! -d "$SERVICE_DIR" ]; then
  echo "ERROR: Service directory not found: $SERVICE_DIR"
  exit 1
fi

echo "=== Continuous Review: ${SERVICE} ($(date -u +%Y-%m-%dT%H:%M:%SZ)) ==="

# Gather file list for context
FILES=$(find "$SERVICE_DIR" -name "*.cs" \
  ! -path "*/bin/*" \
  ! -path "*/obj/*" \
  ! -path "*/Migrations/*.Designer.cs" \
  ! -path "*ModelSnapshot.cs" \
  | sort)

FILE_COUNT=$(echo "$FILES" | wc -l | tr -d ' ')
echo "Files to review: $FILE_COUNT"
echo ""

PROMPT=$(cat <<'PROMPT_EOF'
You are a Principal Engineer performing an exhaustive code review of a .NET microservice.
This is NOT a surface-level review. Dig deep. Be thorough. Be critical.

## Review Scope (ALL of these, not just architecture)

### 1. Bugs & Logic Errors
- Off-by-one errors, null dereferences, incorrect conditionals
- Async/await misuse (fire-and-forget, missing ConfigureAwait in libraries)
- Disposed objects used after disposal
- Incorrect LINQ (FirstOrDefault without null check, etc.)
- String comparison without StringComparison
- DateTime vs DateTimeOffset misuse

### 2. Edge Cases & Boundary Conditions
- Empty collections, null inputs, zero values
- Integer overflow, division by zero
- Concurrent access to shared state
- What happens when external services are down?
- Maximum payload sizes, unbounded growth

### 3. Incomplete Features & Dead Code
- Methods that return hardcoded values or throw NotImplementedException
- TODO/HACK/FIXME comments indicating unfinished work
- Unreachable code paths, unused parameters
- Partial implementations (interface methods that do nothing)

### 4. Security Vulnerabilities
- SQL injection, command injection
- Missing authorization checks
- Sensitive data in logs
- IDOR vulnerabilities (user A accessing user B's data)
- Missing input validation at API boundaries
- Secrets or keys in code

### 5. Data Integrity & Consistency
- Missing database constraints that code assumes exist
- Race conditions between check-and-act
- Orphaned records (parent deleted, children remain)
- Missing cascade deletes or soft-delete propagation
- Decimal precision loss in financial calculations

### 6. Error Handling Gaps
- Swallowed exceptions (empty catch blocks)
- Generic catch-all without proper logging
- Missing retry logic for transient failures
- Exceptions that leak internal state to callers

### 7. Performance & Scalability
- N+1 query patterns
- Unbounded queries (missing Take/pagination)
- Large object allocations in hot paths
- Missing cancellation token propagation
- Blocking calls in async methods (.Result, .Wait())

### 8. Domain Logic Correctness
- Business rules that can be violated
- State transitions that skip validation
- Missing domain events for important state changes
- Aggregate boundaries violated (reaching into other aggregates)

### 9. API Contract Issues
- Breaking changes in public DTOs
- Missing validation attributes
- Inconsistent error response formats
- Missing or incorrect HTTP status codes

### 10. Test Coverage Gaps
- Critical paths without test coverage
- Tests that always pass (tautological assertions)
- Missing negative test cases
- Integration points without contract tests

## Output Format

For each finding, use this format:

### [SEVERITY]: Brief title
- **File**: path/to/file.cs:line_number
- **Issue**: What's wrong
- **Impact**: What could go wrong in production
- **Fix**: Concrete suggestion (code if possible)

Severity levels:
- **CRITICAL**: Will cause data loss, security breach, or system failure
- **HIGH**: Likely to cause bugs in production under normal load
- **MEDIUM**: Edge case that will eventually hit, or maintainability concern
- **LOW**: Code quality, style, or minor optimization

## CRITICAL: prefix any critical finding section header with "## CRITICAL:" so CI can detect it.

At the end, provide:
## Summary
- Total findings by severity
- Top 3 most urgent items
- Overall service health rating (1-10)

PROMPT_EOF
)

# Build file contents for context (skip files > 500 lines)
CONTEXT=""
while IFS= read -r f; do
  LINES=$(wc -l < "$f")
  if [ "$LINES" -le 500 ]; then
    CONTEXT+="
--- $f ---
$(cat "$f")
"
  fi
done <<< "$FILES"

echo "$PROMPT

Review the service at: $SERVICE_DIR

$CONTEXT" | claude --print --model claude-sonnet-4-20250514
