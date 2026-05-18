# Haworks Platform — Agent Instructions

## FIRST: Read These Files
1. `docs/agent-briefs/agent-standards.md` — 12 categories of non-negotiable rules
2. `.claude/projects/*/memory/MEMORY.md` — current state, file paths, decisions
3. `docs/agent-briefs/` — task-specific specs (check before starting any task)

## Anti-Loop Rules
- If something fails twice with the same approach, STOP and try a different approach
- If a build has >5 errors, fix ONE at a time — build after each fix
- If editing a file >500 lines, use grep to find the exact line first — never read the whole file
- If sed/regex breaks a file, `git checkout -- <file>` and use a different method
- NEVER batch edits then build — edit ONE thing, build, confirm, move on
- If stuck for >3 tool calls on the same problem, state what's blocking and ask

## Build Loop (mandatory after every edit)
```
1. Edit file
2. dotnet build <changed-project>.csproj -v q 2>&1 | tail -3
3. If errors: fix → goto 2
4. If clean: next edit
```

## Quick Reference

### Project
- .NET 9.0 microservices, Clean Architecture, 16 services
- Solution: `HaworksPlatform.sln`, per-service filters: `filters/*.slnf`
- Branch: always feature branch, never main directly

### Build
| Command | What | Time |
|---------|------|------|
| `dotnet build src/<Svc>/<Svc>.Api/<Svc>.Api.csproj -v q` | One service | ~10s |
| `dotnet build HaworksPlatform.sln -v q` | Full solution | ~60s |
| `dotnet test tests/<Svc>/<Svc>.Unit/ --no-build` | One test suite | ~5s |

### NEVER DO
- `rm -rf src/` — destroyed repo once
- `sed` for multi-line C# — mangles braces
- `cat >>` to append to .ts/.cs — appends outside closures
- `SaveChangesAsync()` in MassTransit consumers
- `BeginTransactionAsync()` in consumers
- `continue-on-error` to hide test failures
- Work on main without a branch

### Test Categories + CI Filter
- Unit: `tests/<Svc>/<Svc>.Unit/` — no Docker
- Integration: `tests/<Svc>/<Svc>.Integration/` — needs Docker
- E2E: `tests/E2E/` — full Aspire stack, dedicated CI job
- CI fast step filter: `FullyQualifiedName!~Integration&FullyQualifiedName!~E2E&FullyQualifiedName!~Smoke`

### Roslyn Analyzers > Arch Guards
- Analyzers: `src/Analyzers/Haworks.Architecture.Analyzers/`
- Compile-time, zero false positives, IDE squiggles
- When adding a rule: add to Diagnostics.cs + Rules/ + test + delete arch guard duplicate

### MassTransit Laws
1. No `SaveChangesAsync` in consumers — outbox commits automatically
2. No `BeginTransactionAsync` in consumers — conflicts with outbox
3. No `Guid.NewGuid()` inside Polly retry — key changes per attempt
4. No DB locks across external API calls — use ThreePhaseHandlerBase
5. No events without `SaveChangesAsync` in non-consumer code

### Portfolio Site
- Separate repo: `portfolio-site/`
- URL: `https://haworks-platform.pages.dev`
- Stack: Astro 6 + React 18 + Tailwind
- Quality: `bash scripts/check-quality.sh`
