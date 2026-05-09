You are a coding agent. Read carefully — these instructions are the contract.

================================================================
STEP 1 — SETUP (idempotent, parallel-safe via git worktree)
================================================================
The worktree already exists at /Users/chidionyema/Documents/code/rw-audit on branch feat/audit-service. Verify and proceed.

  set -euo pipefail
  WORKTREE=/Users/chidionyema/Documents/code/rw-audit
  cd "$WORKTREE"
  CURRENT=$(git rev-parse --abbrev-ref HEAD)
  [ "$CURRENT" = "feat/audit-service" ] || { echo "ERROR: expected feat/audit-service, on $CURRENT" >&2; exit 1; }
  echo "Worktree ready: $WORKTREE on feat/audit-service"

================================================================
STEP 2 — READ (in this order, in full, BEFORE WRITING ANYTHING)
================================================================
  1. docs/agent-briefs/audit/README.md          (protocol, anti-spiral rules,
                                                  done-report and blocker formats)
  2. docs/agent-briefs/audit/L1D-export-partition-cron.md     (your specific task)

Then read every file in the brief's "Inputs" section, in the order listed.
Do not grep blindly. Do not skim.

================================================================
STEP 3 — EXECUTE
================================================================
Implement L1D-export-partition-cron.md's "Deliverable" list. Run the "Acceptance" commands. Commit per the brief's commit block.

================================================================
STEP 4 — REPORT
================================================================
Paste the done-report template from README.md, filled in. If you hit a blocker, paste the blocker template instead and STOP.
