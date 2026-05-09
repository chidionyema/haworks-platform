# Gemini CLI agent protocol — Audit service

You are a Gemini CLI agent. Read this file **first**, then read the assigned brief (`L<n>-*.md`), then read the brief's **Inputs** section, then start work.

The authoritative design for everything below is `docs/agent-briefs/audit-service-spec.md`. Per-phase briefs reference sections of it. When the spec and a brief disagree, the spec wins; pause and call it out.

## How a brief is structured

Every brief has these sections, in this order:

1. **Goal** — one sentence.
2. **Phase / blocks-on** — which phase you're in and which prior briefs must be done.
3. **Inputs** — exact file paths to read, in order. **Read these all before writing any code.** Don't grep blindly.
4. **Deliverable** — files to create or modify. Concrete checklist; don't go beyond it.
5. **Acceptance** — shell commands that must pass. **Non-negotiable.** Run them yourself before reporting done.
6. **Hard stops** — explicit "do not do X".
7. **Done-report format** — paste back exactly the template below, filled in.

## Anti-spiral rules

- **Read Inputs first.** Most failures come from skimming Inputs and grepping the wrong things.
- **45-minute time budget per phase.** If you're 45 min in and not approaching green, stop. Emit a blocker (format below). Do not keep retrying the same approach.
- **No cross-phase edits.** L1.B does not patch L1.A's extractors; if L1.A is wrong, file a blocker.
- **No silent scope expansion.** If you spot a refactor that "would only take a minute," do not do it. Note it in the done-report under "out-of-scope observations."
- **Trust but verify the spec.** If the spec says X but the code says Y, the spec is the source of truth — but pause and call it out. Don't silently rewrite the spec; don't silently break from it.
- **Don't fabricate paths.** If a file the Inputs list claims exists doesn't, file a blocker. Don't invent.
- **Commit per phase.** Each phase ends with a commit. The next phase rebases nothing — it starts from the previous commit.
- **No solution-wide builds.** `dotnet build RitualworksPlatform.sln` is forbidden — too risky on origin/main where unrelated WIP can break it. Build only the projects in the brief's Deliverable list, plus `deploy/aspire/RitualworksPlatform.AppHost.csproj` to verify wiring.

## Done-report format

Paste this verbatim, filled in:

```
## Brief L<n> — done

### Files created
- path/to/new/file.cs
- ...

### Files modified
- path/to/existing/file.cs (added X)
- ...

### Acceptance
- `<command 1>`: ✓ / ✗
- `<command 2>`: ✓ / ✗

### Commit
- <short hash> <subject line>

### Out-of-scope observations
(things you noticed that need follow-up, but did NOT touch)
- ...

### Blockers
(empty if none, otherwise: what failed, what you tried, what you need)
- ...
```

## Blocker format (if you can't finish)

```
## Brief L<n> — BLOCKED

### What I tried
- step 1 (cmd / file edit) → outcome
- step 2 → outcome

### Where I'm stuck
(one paragraph, no waffle)

### What I need
(specific: a file's content, a missing dependency, a clarification on the spec)

### Files left in flight
(uncommitted edits the reviewer will want to see)
- ...
```

## Phase order

| Brief | Title                          | Blocks-on |
| ----- | ------------------------------ | --------- |
| L0    | Skeleton + DI + Aspire wiring  | (none)    |
| L1.A  | Extractors + redactor          | L0        |
| L1.B  | Capture pipeline               | L0, L1.A  |
| L1.C  | Query API                      | L0, L1.B  |
| L1.D  | Export job + partition cron    | L0, L1.B  |

L1.C and L1.D can be done in either order after L1.B; the table just lists their hard prerequisites.
