# SUPERPROMPT — universal parallel-agent launcher

A single prompt template that any agent (Gemini CLI, Claude, etc.) can run, against any "brief" file in this repo, to do a scoped piece of work in an isolated git worktree without colliding with sibling agents.

The operator fills in the **Parameters** block, hands the whole document to the agent, and the agent executes it top-to-bottom. The contract is: as long as the agent obeys the file-scope rules, two or more agents pointing at sibling briefs can run in true parallel.

---

## Parameters (operator fills these in before handing to the agent)

```
REPO_ROOT=/Users/chidionyema/Documents/code/ritualworks-platform
BASE_BRANCH=feat/audit-service                        # the branch the worktree forks from
BRIEF_FILE=docs/agent-briefs/audit/L1A-extractors-redactor.md
TRACK_ID=audit-L1A                                    # short slug, used in worktree path + branch name
TIME_BUDGET_MINUTES=45
WORKTREE_PARENT=/Users/chidionyema/Documents/code     # where rw-<TRACK_ID> will be created
```

If any parameter is unset, **STOP** and ask. Do not invent defaults.

The branch the agent commits on is derived: `BRANCH=${BASE_BRANCH}-${TRACK_ID}`. If `BASE_BRANCH` already ends in `${TRACK_ID}`, use `BASE_BRANCH` as-is.

---

## Contract — what this prompt guarantees

1. **Isolation.** The agent works in its own worktree (`${WORKTREE_PARENT}/rw-${TRACK_ID}`). Sibling agents can run concurrently without filesystem conflicts.
2. **Scope discipline.** The brief's Deliverable list defines what files the agent may create / modify. `git status` after the work runs must list ONLY those files. Anything else = scope violation = blocker.
3. **No silent failures.** Every Acceptance command in the brief must exit 0. If one fails, the agent debugs within its scope; if it can't, it emits a Blocker report and STOPS.
4. **Single commit per track.** One commit per agent run, on the dedicated branch. Iterations within the run use `git commit --amend` until the run is reported complete.
5. **Standard reporting.** The agent ends with either the Done-Report template (success) or the Blocker template (couldn't finish).

---

## Phase 0 — Pre-flight (≤ 30 seconds)

```bash
set -euo pipefail

# Validate every parameter is set and non-empty
for var in REPO_ROOT BASE_BRANCH BRIEF_FILE TRACK_ID TIME_BUDGET_MINUTES WORKTREE_PARENT; do
    [ -n "${!var:-}" ] || { echo "ERROR: $var is unset" >&2; exit 1; }
done

# Repo + brief must exist
[ -d "$REPO_ROOT/.git" ] || [ -f "$REPO_ROOT/.git" ] || { echo "ERROR: $REPO_ROOT is not a git repo" >&2; exit 1; }
[ -f "$REPO_ROOT/$BRIEF_FILE" ] || { echo "ERROR: brief not found at $REPO_ROOT/$BRIEF_FILE" >&2; exit 1; }

# Base branch must resolve
git -C "$REPO_ROOT" rev-parse --verify "$BASE_BRANCH" >/dev/null 2>&1 \
    || git -C "$REPO_ROOT" rev-parse --verify "origin/$BASE_BRANCH" >/dev/null 2>&1 \
    || { echo "ERROR: $BASE_BRANCH does not exist locally or on origin" >&2; exit 1; }

WORKTREE="$WORKTREE_PARENT/rw-$TRACK_ID"
# Derive BRANCH: if BASE_BRANCH already ends in -$TRACK_ID, reuse it; else append
case "$BASE_BRANCH" in
    *-"$TRACK_ID") BRANCH="$BASE_BRANCH" ;;
    *)             BRANCH="${BASE_BRANCH}-${TRACK_ID}" ;;
esac

echo "Pre-flight OK"
echo "  REPO_ROOT       = $REPO_ROOT"
echo "  BASE_BRANCH     = $BASE_BRANCH"
echo "  WORKTREE        = $WORKTREE"
echo "  BRANCH          = $BRANCH"
echo "  BRIEF_FILE      = $BRIEF_FILE"
echo "  TIME_BUDGET     = $TIME_BUDGET_MINUTES minutes"
```

If any of those fail, **STOP** and emit a Blocker report. Do not patch around missing parameters.

---

## Phase 1 — Worktree setup (≤ 2 minutes)

Idempotent — re-runs are safe.

```bash
cd "$REPO_ROOT"
git fetch origin --prune --quiet || true   # best-effort; offline runs still proceed

if [ ! -d "$WORKTREE" ]; then
    # If branch already exists locally, use it; otherwise create it from BASE_BRANCH
    if git rev-parse --verify "$BRANCH" >/dev/null 2>&1; then
        git worktree add "$WORKTREE" "$BRANCH"
    elif git rev-parse --verify "origin/$BRANCH" >/dev/null 2>&1; then
        git worktree add "$WORKTREE" -b "$BRANCH" "origin/$BRANCH"
    else
        # Resolve BASE_BRANCH (local first, then origin)
        if git rev-parse --verify "$BASE_BRANCH" >/dev/null 2>&1; then
            BASE_REF="$BASE_BRANCH"
        else
            BASE_REF="origin/$BASE_BRANCH"
        fi
        git worktree add "$WORKTREE" -b "$BRANCH" "$BASE_REF"
    fi
fi

cd "$WORKTREE"
CURRENT=$(git rev-parse --abbrev-ref HEAD)
[ "$CURRENT" = "$BRANCH" ] || { echo "ERROR: expected $BRANCH, on $CURRENT" >&2; exit 1; }
echo "Worktree ready: $WORKTREE on $BRANCH"

# Snapshot the working tree BEFORE work begins — used in Phase 5 to verify scope
git status --porcelain > /tmp/superprompt-pre-work-status-$TRACK_ID.txt
```

---

## Phase 2 — Read (≤ 15 min, hard cap)

Read these files in this order, **in full**, **before writing or editing anything**:

1. The brief's directory's `README.md`, if it exists (e.g., `docs/agent-briefs/audit/README.md`). It carries the protocol + done-report templates for that brief family.
2. The brief itself: `$BRIEF_FILE`.
3. Every file the brief lists under "Inputs", in the order listed.

Do NOT:
- Grep blindly for things the brief doesn't ask for.
- Skim. If you can't recall what an Input file said, re-read it.
- Read files outside the Inputs list "to get more context". The Inputs list is the deliberate context window. Trust it.

If an Input file the brief claims exists doesn't, **STOP** and emit a Blocker.

---

## Phase 3 — Execute (within the time budget)

Implement the brief's "Deliverable" list, exactly:
- Create the listed new files with the listed contents.
- Modify the listed existing files only as the brief specifies.
- Do NOT touch any file outside the Deliverable list. If you find yourself wanting to, that's a scope violation — emit a Blocker instead.

While working:
- **Time budget.** If you've burned `TIME_BUDGET_MINUTES` and are not approaching green, **STOP** and emit a Blocker. Do not keep retrying the same approach.
- **No drive-by refactors.** If you spot something that "would only take a minute," log it under "out-of-scope observations" in the done-report. Do not change it.
- **No package additions outside the brief.** If a NuGet / npm / etc. package isn't already in the project's dependency list, do not add it unless the brief explicitly says to.
- **No skipping tests.** If a test in the brief's Acceptance fails, fix the code (or the test if it's clearly wrong), don't `[Fact(Skip=…)]` it.

---

## Phase 4 — Verify

Run **every** command in the brief's "Acceptance" section. Capture exit codes.

```bash
# Example shape; substitute the brief's actual commands.
dotnet build src/X/X.csproj -c Release --nologo --verbosity quiet
dotnet test  tests/X.Unit/X.Unit.csproj -c Release --logger "console;verbosity=minimal"
```

Every Acceptance command must exit 0. If one fails:
1. Re-read the failing command's output. The error is usually a missing reference, a typo, or a real test failure.
2. Fix within scope. Don't expand to other phases' files.
3. If you can't fix within scope and within remaining time budget, **STOP** and emit a Blocker.

---

## Phase 5 — Scope verification + Commit

Before committing, verify your changes are inside scope:

```bash
cd "$WORKTREE"

# What's actually changed?
git status --porcelain | sort > /tmp/superprompt-post-work-status-$TRACK_ID.txt
diff /tmp/superprompt-pre-work-status-$TRACK_ID.txt /tmp/superprompt-post-work-status-$TRACK_ID.txt
```

Compare the file list to the brief's Deliverable list. Every changed file MUST be on the brief's Deliverable list. If any unexpected file appears (incl. accidental edits, IDE artifacts, build output not in `.gitignore`), **STOP** and emit a Blocker; do not proceed to commit.

When clean, commit:

```bash
git add <ONLY the files in the brief's Deliverable list>   # NOT git add -A; be explicit
git commit -m "$(cat <<'EOF'
<commit message from the brief's commit block>
EOF
)"
```

The brief specifies the exact commit message. Do not freelance.

If the brief is silent on the commit message, derive: `<type>(<area>/<TRACK_ID>): <one-line summary>` (matching the repo's existing commit-message style).

**One commit per track.** Iterations within the same run use `git commit --amend` (only on the unpushed commit on this branch).

---

## Phase 6 — Report

Emit one of the two templates below, filled in.

### Done-report (success path)

```
## Track <TRACK_ID> — done

### Files created
- <path>
- ...

### Files modified
- <path> (one-line summary of the change)
- ...

### Acceptance
- `<acceptance command 1>`: ✓ / ✗
- `<acceptance command 2>`: ✓ / ✗
(every command from the brief, with the actual outcome)

### Commit
- <short hash> <subject line>

### Out-of-scope observations
(things you noticed that need follow-up, but did NOT touch — empty if none)
- ...

### Blockers
(empty if none, otherwise — but if any, you should have used the Blocker template instead)
```

### Blocker template (any failure path)

```
## Track <TRACK_ID> — BLOCKED

### What I tried
- step 1 (cmd / file edit) → outcome
- step 2 → outcome

### Where I'm stuck
(one paragraph, no waffle)

### What I need
(specific: a missing file, a clarification, a sibling track's interface, …)

### Files left in flight
(uncommitted edits the reviewer will want to see)
- <path>
- ...

### Time spent
<minutes> of <TIME_BUDGET_MINUTES>
```

---

## Anti-spiral rules (non-negotiable)

These exist because LLM coding agents fail in predictable ways. Internalize them.

- **Read inputs first.** Most failures come from skimming Inputs and grepping the wrong things.
- **45-minute default time budget.** The operator may override via `TIME_BUDGET_MINUTES`. If you hit it and aren't green, STOP and emit a Blocker. Do not keep retrying the same approach.
- **No cross-track edits.** If your track's work needs a sibling track to do something it currently doesn't, you do **not** patch the sibling's code. You file a Blocker.
- **No silent scope expansion.** If you spot a refactor that "would only take a minute," log it under "out-of-scope observations." Do not change it.
- **Trust but verify the spec.** If the spec says X but the code says Y, the spec is the source of truth — pause and call it out in the done-report. Don't silently rewrite the spec; don't silently break from it.
- **Don't fabricate paths.** If a file the Inputs list claims exists doesn't, file a Blocker. Don't invent.
- **Don't drop stashes without inspecting.** If you need to clear a stash, run `git stash show -p stash@{N}` first; if it shows changes, treat it as live. Recovery from a mistakenly-dropped stash is via `git fsck --lost-found`.
- **Never push without explicit operator instruction.** Your job is to commit on the dedicated branch and report. Pushing is the operator's call.
- **Never merge to main.** Same reason.
- **Never `git clean -fdx` or `git reset --hard`.** They destroy untracked files and unstaged work. If the working tree is in a state you don't understand, file a Blocker.

---

## Operator usage — running 1..N agents in parallel

For each parallel track:

1. Fill in the Parameters block (different `TRACK_ID` per agent, different `BRIEF_FILE`).
2. Hand the filled-in superprompt to the agent.
3. Wait for the done-report or blocker.
4. Review the done-report's "Files modified" against the brief's Deliverable list — they must match.
5. After all parallel tracks report done, merge each branch back into `BASE_BRANCH` in turn:

```bash
cd "$REPO_ROOT"
git checkout "$BASE_BRANCH"
for branch in "${BASE_BRANCH}-trackA" "${BASE_BRANCH}-trackB" "${BASE_BRANCH}-trackC"; do
    git merge --no-ff "$branch" -m "merge $branch"
done
```

Conflicts at merge-back time mean a track violated its scope. Reject and re-run with the violation called out.

---

## How to write a brief that's superprompt-compatible

If you're authoring a new brief for an agent to consume via this superprompt, the brief MUST have these sections (matching `docs/agent-briefs/audit/L0-skeleton.md` as a template):

1. **Goal** — one sentence.
2. **Phase / blocks-on** — for ordering.
3. **Inputs** — exact file paths, in read-order, that the agent must read in full.
4. **Deliverable** — explicit list of files to create / modify. Files NOT on this list are out-of-scope.
5. **Acceptance** — shell commands. Every one must exit 0. The agent runs them verbatim.
6. **Hard stops** — including a "parallel-scope" subsection that lists the files this track exclusively owns (when running in a parallel family).
7. **Done-report format** — the standard template (or a brief-specific extension).
8. **Commit block** — exact `git commit` invocation, with a HEREDOC commit message.

The superprompt enforces the contract; the brief carries the substance.

---

## Why this exists

Earlier in the platform's history, parallel agents stepped on each other's toes via:
- shared DI registration files (one agent's `services.AddX` overwrote another's),
- the same `Program.cs` block (concurrent edits = merge conflict),
- shared `csproj` files (concurrent package additions = merge conflict),
- shared migrations (timestamps collided),
- accidental `git add -A` pulling in another track's untracked work.

The superprompt + the brief's Deliverable + the parallel-scope hard-stops together prevent every one of those by contract:
- Each track owns disjoint files (declared in the brief).
- Each track stages only the files it owns (`git add <explicit-list>`, never `-A`).
- The pre-commit scope check catches any drift.
- Worktrees keep the filesystems separate.

If a merge conflicts at the end, the contract was violated — the superprompt run that violated it is the one to reject and re-run.
