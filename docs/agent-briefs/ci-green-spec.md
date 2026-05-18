# Mission: CI Green + Deploy — Agent Execution Spec

## Objective
Get ALL CI jobs green so the deploy workflow triggers and the backend goes live on Fly.io.

## Current State (2026-05-18)
- **Build + Unit + ArchGuards**: GREEN ✓
- **Integration tests**: 10/14 suites FAILING ✗
- **Passing**: CheckoutOrchestrator, Catalog, Search, (1 more)
- **Agent branch `fix/integration-tests-green`** has partial fixes — MERGE FIRST

## The 10 Failing Integration Suites

All fail for the SAME 3 root causes:

### Root Cause 1: JWKS Auth (6 suites)
**Error**: `IDX20108 — http address not valid for HTTPS scheme`
**Suites**: BffWeb, Orders, Notifications, Payments, Webhooks, Payouts
**Why**: The real JWT middleware tries to fetch JWKS from `http://test-identity.invalid` but requires HTTPS.
**Fix**: Each `WebAppFactory` must override auth with `TestAuthenticationHandler`:
```csharp
builder.ConfigureTestServices(services =>
{
    services.AddAuthentication(TestAuthenticationHandler.SchemeName).AddTestAuth();
});
```
**Working example**: `tests/CheckoutOrchestrator/CheckoutOrchestrator.Integration/CheckoutWebAppFactory.cs`

### Root Cause 2: Missing Config (2 suites)
**Suites**: Audit (`ConnectionStrings:s3`), Identity (`Cors:AllowedOrigins`)
**Fix**: Add missing config keys in `ConfigureAppConfiguration`:
```csharp
builder.ConfigureAppConfiguration((ctx, config) =>
{
    config.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:s3"] = "http://localhost:4566",  // LocalStack
        ["Cors:AllowedOrigins:0"] = "http://localhost:4321",
        // ... any other required keys
    });
});
```

### Root Cause 3: Missing Project (1 suite)
**Suite**: Scheduler
**Error**: `MSB1009: Project file does not exist`
**Fix**: Check CI matrix in `.github/workflows/ci.yml` — either create the project or remove from matrix.

### Root Cause 4: Content (1 suite)
**Suite**: Content — likely same auth/config issue as above.

## Execution Steps (in order)

### Step 1: Merge agent branch
```bash
git checkout main && git pull
gh pr create --title "fix: integration tests" --base main --head fix/integration-tests-green
gh pr merge --squash
```

### Step 2: Find all WebAppFactory files
```bash
find tests/ -name "*WebAppFactory*.cs" -o -name "*Factory*.cs" | grep Integration
```

### Step 3: For each FAILING factory, apply this checklist

Read the file first. Then ensure it has ALL of:

- [ ] `JwtTestDefaults.SetTestEnvironmentVariables()` in `InitializeAsync`
- [ ] `services.AddAuthentication(TestAuthenticationHandler.SchemeName).AddTestAuth()` in `ConfigureTestServices`
- [ ] `config.AddInMemoryCollection(...)` with ALL required connection strings
- [ ] Environment variable `ASPNETCORE_ENVIRONMENT=Test`
- [ ] Environment variable `Vault:Enabled=false`

Compare with CheckoutWebAppFactory.cs which PASSES.

### Step 4: Fix Scheduler
Check `.github/workflows/ci.yml` for the integration matrix:
```bash
grep -A30 "integration:" .github/workflows/ci.yml
```
Either create `tests/Scheduler/Scheduler.Integration/` project or remove "Scheduler" from the matrix.

### Step 5: Build + test locally
```bash
# Build
dotnet build HaworksPlatform.sln -v q

# Run each fixed suite individually
dotnet test tests/Identity/Identity.Integration/ --logger "console;verbosity=minimal"
dotnet test tests/Orders/Orders.Integration/ --logger "console;verbosity=minimal"
dotnet test tests/Payments/Payments.Integration/ --logger "console;verbosity=minimal"
dotnet test tests/BffWeb/BffWeb.Integration/ --logger "console;verbosity=minimal"
dotnet test tests/Notifications/Notifications.Integration/ --logger "console;verbosity=minimal"
dotnet test tests/Audit/Audit.Integration/ --logger "console;verbosity=minimal"
dotnet test tests/Webhooks/Webhooks.Integration/ --logger "console;verbosity=minimal"
dotnet test tests/Payouts/Payouts.Integration/ --logger "console;verbosity=minimal"
dotnet test tests/Content/Content.Integration/ --logger "console;verbosity=minimal"
```

### Step 6: Commit + push + verify CI
```bash
git add -A
git commit -m "fix: all integration tests green — auth + config fixes across 10 factories"
git push
# Watch CI: gh run watch
```

## Rules
- Work on a SEPARATE BRANCH (`fix/ci-all-green`), not main
- Read EVERY file before editing
- Don't change test assertions — only fix infrastructure (auth, config, connection strings)
- Don't skip or exclude tests
- Run each suite locally after fixing before committing
- The fix is ALWAYS the same pattern — copy from CheckoutWebAppFactory

## Files to Reference
- Working factory: `tests/CheckoutOrchestrator/CheckoutOrchestrator.Integration/CheckoutWebAppFactory.cs`
- TestAuthHandler: `src/BuildingBlocks.Testing/Authentication/TestAuthenticationHandler.cs`
- JwtTestDefaults: `src/BuildingBlocks.Testing/Authentication/JwtTestDefaults.cs`
- CI workflow: `.github/workflows/ci.yml`
