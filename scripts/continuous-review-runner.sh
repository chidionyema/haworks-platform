#!/usr/bin/env bash
set -euo pipefail

# Continuous Code Review Runner (local cron)
# 3-phase pipeline:
#   Phase 1: Deep review of a service
#   Phase 2: Self-review (validate findings, discard false positives)
#   Phase 3: Fix confirmed issues, commit, PR with report + fixes

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

SERVICES=(
  Audit BffWeb Catalog CheckoutOrchestrator Identity
  Location Media Merchant Notifications Orders
  Payments Payouts Pricing Privacy Search
  Shipping Webhooks Scheduler RulesEngine Realtime
)

STATE_FILE="$REPO_ROOT/docs/reviews/.review-state"
REPORT_DIR="$REPO_ROOT/docs/reviews"

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
REPORT_FILE="$SERVICE_REPORT_DIR/${DATE}.md"
VALIDATED_FILE="$SERVICE_REPORT_DIR/${DATE}-validated.md"

# ============================================================
# PHASE 1: Deep Review
# ============================================================
echo ">>> [Phase 1/3] Reviewing: $SERVICE ($(date))"
echo ">>> Report: $REPORT_FILE"

bash "$REPO_ROOT/scripts/continuous-review.sh" "$SERVICE" > "$REPORT_FILE" 2>&1
EXIT_CODE=$?

if [ $EXIT_CODE -ne 0 ]; then
  echo ">>> Review failed with exit code $EXIT_CODE"
  exit $EXIT_CODE
fi

PHASE1_COUNT=$(grep -c "^###" "$REPORT_FILE" 2>/dev/null || true)
PHASE1_COUNT=${PHASE1_COUNT:-0}
echo ">>> Phase 1 complete: $PHASE1_COUNT findings"

# ============================================================
# PHASE 2: Self-Review (validate findings, discard false positives)
# ============================================================
echo ">>> [Phase 2/3] Validating findings..."

VALIDATE_PROMPT=$(cat <<'VALIDATE_EOF'
You are a senior engineer validating a code review. You have the original review findings below.
For EACH finding, you must:

1. Read the actual source file mentioned and verify the issue exists at the stated line
2. Classify each finding as:
   - CONFIRMED: The issue is real, the line numbers match, the impact is accurate
   - FALSE_POSITIVE: The code is actually correct, or the issue doesn't exist at that location
   - OVERSTATED: The issue exists but severity is wrong (state correct severity)
   - DUPLICATE: Same underlying issue as another finding

3. For CONFIRMED findings, refine the fix suggestion to be precise and correct.
4. Remove all FALSE_POSITIVE findings from the final output.

Output the validated review in the SAME format as the input, but:
- Only include CONFIRMED and OVERSTATED (with corrected severity) findings
- Add a confidence score (1-10) to each finding
- At the top, add: "## Validation Summary\n- Original findings: N\n- Confirmed: N\n- False positives removed: N\n- Overstated (downgraded): N"

IMPORTANT: Actually read each file. Do not rubber-stamp the review. Be skeptical.
VALIDATE_EOF
)

echo "$VALIDATE_PROMPT

Here is the review to validate:

$(cat "$REPORT_FILE")
" | claude --print --model claude-sonnet-4-20250514 > "$VALIDATED_FILE" 2>&1

CONFIRMED_COUNT=$(grep -c "^###" "$VALIDATED_FILE" 2>/dev/null || true)
CONFIRMED_COUNT=${CONFIRMED_COUNT:-0}
CONFIRMED_COUNT=$(echo "$CONFIRMED_COUNT" | tr -d '[:space:]')
echo ">>> Phase 2 complete: $CONFIRMED_COUNT confirmed findings"

if [ "$CONFIRMED_COUNT" -eq 0 ]; then
  echo ">>> No confirmed findings. Skipping fix phase."
  rm -f "$VALIDATED_FILE"
  exit 0
fi

# ============================================================
# PHASE 3: Fix confirmed issues
# ============================================================
echo ">>> [Phase 3/3] Fixing confirmed issues..."

BRANCH="review/${SERVICE,,}-${DATE}"
ORIGINAL_BRANCH=$(git branch --show-current)

# Save reports before switching branches
REPORT_CONTENT=$(cat "$REPORT_FILE")
VALIDATED_CONTENT=$(cat "$VALIDATED_FILE")

git stash --include-untracked -m "continuous-review: stash before branch" 2>/dev/null || true
git checkout -b "$BRANCH" main

# Recreate reports on clean branch
mkdir -p "$SERVICE_REPORT_DIR"
echo "$REPORT_CONTENT" > "$REPORT_FILE"
echo "$VALIDATED_CONTENT" > "$VALIDATED_FILE"
mkdir -p "$(dirname "$STATE_FILE")"
echo "$INDEX" > "$STATE_FILE"

# Run Claude Code to fix the confirmed issues
FIX_PROMPT=$(cat <<FIX_EOF
You are fixing confirmed code review findings in the $SERVICE service.
The validated review is below. Fix ONLY CONFIRMED findings with confidence >= 7.

Rules:
- Make minimal, surgical fixes. Do not refactor surrounding code.
- Do not add comments explaining the fix unless the logic is non-obvious.
- Do not fix LOW severity items.
- If a fix requires adding a dependency or changing an interface used by other services, SKIP it and note why.
- If a fix is ambiguous or risky, SKIP it.
- Run \`dotnet build filters/${SERVICE}.slnf\` after all fixes to verify compilation.
- If the build fails, revert the failing fix and move on.

After fixing, output a summary:
## Fixes Applied
- [file:line] Brief description of fix

## Skipped (too risky or ambiguous)
- [finding] Reason skipped

$(cat "$VALIDATED_FILE")
FIX_EOF
)

FIX_OUTPUT=$(echo "$FIX_PROMPT" | claude --model claude-sonnet-4-20250514 2>&1)
FIX_SUMMARY=$(echo "$FIX_OUTPUT" | tail -50)

echo "$FIX_SUMMARY" > "$SERVICE_REPORT_DIR/${DATE}-fixes.md"

# Stage everything: report + validated report + code fixes
git add -A
git status --short

git commit -m "review(${SERVICE}): review + fix ${DATE}

Phase 1: Deep review ($( grep -c "^###" "$REPORT_FILE" || echo 0) findings)
Phase 2: Self-validation ($CONFIRMED_COUNT confirmed)
Phase 3: Automated fixes applied

Co-Authored-By: Claude Code <noreply@anthropic.com>"

git push -u origin "$BRANCH"

# Build PR body
PR_BODY=$(cat <<EOF
## Continuous Review + Fix: ${SERVICE}

**Date**: ${DATE}
**Pipeline**: Review -> Validate -> Fix

### Validation Summary
$(head -10 "$VALIDATED_FILE")

### Fixes Applied
$(cat "$SERVICE_REPORT_DIR/${DATE}-fixes.md" 2>/dev/null || echo "See commit diff")

### Full Validated Review
<details>
<summary>Click to expand</summary>

$(cat "$VALIDATED_FILE" | head -300)

</details>

---
Generated by \`scripts/continuous-review-runner.sh\` (3-phase pipeline)
EOF
)

PR_URL=$(gh pr create \
  --title "review(${SERVICE}): validated findings + fixes ${DATE}" \
  --body "$PR_BODY" \
  --label "continuous-review" 2>&1) || true

echo ">>> PR: $PR_URL"

# Create issues only for confirmed findings that were NOT fixed
echo ">>> Creating issues for unfixed findings..."
SEVERITY_PATTERN="^### (CRITICAL|HIGH|MEDIUM):"

while IFS= read -r line; do
  if [[ "$line" =~ $SEVERITY_PATTERN ]]; then
    SEVERITY="${BASH_REMATCH[1]}"
    FINDING_TITLE="${line#*: }"
    ISSUE_TITLE="[${SEVERITY}] ${SERVICE}: ${FINDING_TITLE}"

    # Check if this was fixed (skip if mentioned in fixes file)
    if grep -qi "$FINDING_TITLE" "$SERVICE_REPORT_DIR/${DATE}-fixes.md" 2>/dev/null; then
      echo ">>> Fixed, skipping issue: $FINDING_TITLE"
      continue
    fi

    # Grab context
    ISSUE_BODY=$(grep -A 5 -F "$line" "$VALIDATED_FILE" | tail -n +2)

    # Skip if duplicate
    EXISTING=$(gh issue list --label "continuous-review" --search "\"$ISSUE_TITLE\"" --state open --json number --jq length 2>/dev/null || echo "0")
    if [ "$EXISTING" -eq 0 ]; then
      gh issue create \
        --title "$ISSUE_TITLE" \
        --body "$(printf '%s\n\n---\nSkipped by auto-fix (too risky or ambiguous)\nFrom: %s\nPR: %s' "$ISSUE_BODY" "$VALIDATED_FILE" "${PR_URL:-n/a}")" \
        --label "continuous-review,${SEVERITY,,}" 2>/dev/null \
        && echo ">>> Issue created: $ISSUE_TITLE" \
        || echo ">>> Issue creation failed: $ISSUE_TITLE"
    fi
  fi
done < "$VALIDATED_FILE"

# Return to original branch and restore stash
git checkout "$ORIGINAL_BRANCH" 2>/dev/null || git checkout main
git stash pop 2>/dev/null || true

echo ">>> Done. Next service in rotation: ${SERVICES[$(( (INDEX + 1) % ${#SERVICES[@]} ))]}"
