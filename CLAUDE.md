# Output
- No preamble, no postamble, no restating the request.
- Code tasks: output the change only. No explanation unless asked.
- After edits: one-line summary of changed files. No diff recap.
- Errors: paste the failing line + fix. Don't re-explain.
- Don't narrate what you're about to do — just do it.
- Skip confirmation for safe, reversible actions (edits, reads, tests).

# Reading
- Never read full files. Use Grep/Glob, then offset/limit to target exact lines.
- Vague exploration → spawn haiku subagent, return summary only.
- Don't read: bin/, obj/, node_modules/, .git/, **/generated/, **/*.Designer.cs, *ModelSnapshot.cs
- Don't read test fixtures > 200 lines unless asked.

# Model routing
- Default: sonnet.
- Exploration, log grep, dependency scans, file search: haiku subagent.
- Multi-file refactors, architectural calls: opus only when sonnet stalls.

# Project
- .NET 9.0 microservices, Clean Architecture.
- Architecture, security rules, audit protocol: `.claude/projects/*/memory/`
- Gemini-compatible audit brief: `docs/agent-briefs/audit-protocol.md`

# Commands
- Build: `dotnet build`
- Test project: `dotnet test path/to/Project.Tests.csproj`
- Test single: `dotnet test --filter "FullyQualifiedName~ClassName"`
- Format: `dotnet format`
- Migrations: `dotnet ef database update --project src/[Service]`

# Engineering bar
- Before adding an abstraction, check if an existing one fits.
- Before adding a dependency, check if BuildingBlocks already provides it.
- No TODOs left in committed code. No commented-out code.
- If a fix needs a workaround, flag it explicitly — don't hide it.
# Project
- .NET 9.0 microservices platform (Clean Architecture)
- See `.claude/projects/*/memory/` for full architecture reference
- See `.claude/projects/*/memory/security-rules.md` for mandatory security rules
- See `.claude/projects/*/memory/audit-protocol.md` for 12-lens service audit process
- See `docs/agent-briefs/audit-protocol.md` for the same protocol (Gemini agent compatible)

# Integration Test Rules (ENFORCED BY CI)
- NEVER create raw Testcontainers (PostgreSqlBuilder, ContainerBuilder, etc.) in test projects
- ALWAYS use shared singletons from `BuildingBlocks.Testing.Containers`:
  - `SharedTestPostgres.CreateDatabaseAsync("svc")` — standard Postgres
  - `SharedTestPostGIS.CreateDatabaseAsync("svc")` — PostGIS (geospatial)
  - `SharedTestElasticsearch.GetConnectionAsync("svc")` — Elasticsearch
- Containers use `WithReuse(true)` — one container per type across all test runs
- CI architecture check (`scripts/check-architecture.sh`) will FAIL on raw container usage
