# Agent Standards & Ways of Working

This document defines how ANY AI agent (Gemini, Claude, Copilot) must operate on this codebase. These are non-negotiable rules learned from real production incidents.

## 1. Branch & Worktree Discipline

- **NEVER work directly on `main`** — always create a feature branch
- **Use git worktrees for parallel work** — `git worktree add .claude/worktrees/<name> -b <branch>`
- **Push frequently** — don't accumulate large uncommitted changes
- **One concern per commit** — don't mix bug fixes with features
- **Merge via PR** — even for small fixes, create a PR so CI validates

## 2. Read Before Write

- **NEVER edit a file you haven't read** — understand context first
- **NEVER use sed for multi-line edits** — it mangles brace-balanced code (proven repeatedly)
- **NEVER use `cat >>` to append to structured files** — it appends outside closures
- **Use targeted edits** — find the exact string, replace it, verify
- **Verify after editing** — build immediately after every edit, don't batch

## 3. Build & Test Before Push

- **Build after EVERY edit**: `dotnet build <project> -v q` — fix errors immediately, don't accumulate
- **Run affected tests before pushing**: `dotnet test <test-project> --no-build --logger "console;verbosity=minimal"`
- **Never push code that doesn't compile**
- **The CI filter for unit tests is**: `FullyQualifiedName!~Integration&FullyQualifiedName!~E2E&FullyQualifiedName!~Smoke`
- **Integration tests need Docker** — they use SharedTestPostgres (Testcontainers)
- **E2E tests start the full Aspire AppHost** — they run in a dedicated CI job only

## 4. Test Categorization Rules

- **Unit tests**: No external dependencies. Use InMemory/mocks. Fast (<5s).
- **Integration tests**: Need Docker (Testcontainers). MUST have "Integration" in namespace.
- **E2E tests**: Start full Aspire stack. MUST be in `tests/E2E/`.
- **Smoke tests**: Hit deployed URLs. MUST be in `tests/Smoke/`.
- **NEVER put tests that need infrastructure in "Unit" projects**
- **NEVER use `FromSqlRaw` in unit tests** — it doesn't work with InMemory provider

## 5. Architecture Guard Rules

- **Roslyn analyzers (HWK001-050+) enforce at compile time** — preferred over regex guards
- **Don't duplicate a Roslyn rule with an arch guard test** — Roslyn catches it earlier with zero false positives
- **Arch guard tests exist for things Roslyn CAN'T check**: file existence, cross-project references, saga patterns, deployment config
- **The Analyzers project code contains pattern strings** — always exclude `src/Analyzers/` from regex scans using `IsExcludedFromGuards()`
- **New arch guards must not break on new services** — use allowlists, not denylists

## 6. MassTransit / EF Core Laws (CRITICAL)

These are architectural invariants. Violating them causes data loss or financial bugs.

1. **NEVER call `SaveChangesAsync()` in a MassTransit consumer** — the outbox commits automatically
2. **NEVER call `BeginTransactionAsync()` in a consumer** — conflicts with the outbox ambient transaction
3. **NEVER generate `Guid.NewGuid()` inside Polly `ExecuteAsync`** — the key changes per retry, defeating idempotency
4. **NEVER hold a DB transaction open across an external HTTP call** — use ThreePhaseHandlerBase
5. **NEVER publish events without `SaveChangesAsync` in non-consumer code** — events are outbox rows
6. **NEVER use positional record constructors for MassTransit events** — use `{ get; init; }` properties

## 7. Contract / Event Rules

- Events are in `src/Contracts/` — immutable records with `{ get; init; }`
- Some events use `decimal Amount` (domain), some use `long AmountCents` (cents) — check the actual contract before constructing events in tests
- `required` properties MUST be set in object initializers — missing them causes CS9035
- Always set `Currency` on checkout/payment events — the saga throws if null

## 8. Portfolio Site Rules

- **Tech stack**: Astro 6 + React 18 + Tailwind + Framer Motion
- **Deployed to**: Cloudflare Pages, project name `haworks-platform`
- **Live URL**: `https://haworks-platform.pages.dev`
- **No `as any` casts** — quality gate blocks them
- **No raw `${}` in JSX text** — use `{`template`}` syntax
- **Clipboard API must be in try/catch** — fails in non-HTTPS contexts
- **client:load, not client:only** — SSR hydration is needed for Playwright tests
- **Quality gates**: `bash scripts/check-quality.sh` — runs in CI

## 9. Token Efficiency Rules

- **Don't explore — read memory files first**: `memory/MEMORY.md` has file paths, patterns, decisions
- **Don't read full files** — use `grep`/`offset`/`limit` to target exact lines
- **Don't re-read files you just wrote** — trust the edit
- **Parallelize independent work** — use multiple tool calls or background agents
- **Use sed for simple single-line replacements** — reserve Edit for multi-line changes
- **Build incrementally** — `dotnet build <project>` not `dotnet build HaworksPlatform.sln` when fixing one file
- **Fail fast** — if an approach isn't working after 2 attempts, try a different approach

## 10. Destructive Action Rules

- **NEVER `rm -rf` on `src/` directories** — the repo was destroyed once this way
- **NEVER `git reset --hard` without asking** — work may be lost
- **NEVER suppress Roslyn analyzer errors** — fix the code
- **NEVER add `continue-on-error: true` to hide test failures** — fix the tests
- **NEVER use `// TODO` in committed code** — implement it or don't add it

## 11. CI/CD Pipeline

- **CI**: Build → Unit/Arch/Contract tests → Integration (matrix with Docker) → E2E → Lighthouse → Axe
- **Deploy**: Triggered by CI success on main → Fly.io per-service (path-filtered)
- **Gitleaks**: `.gitleaks.toml` allowlists test fixtures — don't add real secrets
- **Coverage**: Non-fatal (`continue-on-error: true` only for this step)
- **All other steps MUST pass** — no exceptions

## 12. Communication Standards

- **No preamble** — start with the action or answer
- **One-line summary after edits** — "Fixed X in Y"
- **Build output, not plans** — show results, not intentions
- **Flag blockers immediately** — don't silently work around them
- **Commit messages**: `fix:`, `feat:`, `refactor:`, `docs:`, `perf:`, `chore:` prefix
