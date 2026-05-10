# Scaffold template

Pure-boilerplate L0 service skeleton applied by `wave run`. Substitutions:
`{{FEATURE}}` → PascalCase feature name (e.g. `Pricing`); `{{feature}}` → lowercase (e.g. `pricing`).

The DI orchestrator (`{{FEATURE}}.Application/DependencyInjection.cs`) and
per-track stubs (`DependencyInjection.<TrackId>.cs`) are **not** templated
here — `wave run` generates them after the brief is written, because they
depend on the `TRACKS=(...)` declared in the brief.

After `wave run`:
- `src/{{FEATURE}}/` — full 4-project skeleton, compiles via `dotnet build`
- `src/{{FEATURE}}/{{FEATURE}}.Application/DependencyInjection.cs` — orchestrator calling N stubs
- `src/{{FEATURE}}/{{FEATURE}}.Application/DependencyInjection.<TrackId>.cs` — empty stubs (one per track), each owned by exactly one L1 track

Tests:
- `tests/{{FEATURE}}.Unit/` — empty xUnit project
- `tests/{{FEATURE}}.Integration/` — empty xUnit + Testcontainers project (Placeholder.cs keeps it building)

L1 tracks fill in everything else. The orchestrator + stubs are written ONCE at L0 time and never edited by L1 — preserving the parallel-execution contract.
