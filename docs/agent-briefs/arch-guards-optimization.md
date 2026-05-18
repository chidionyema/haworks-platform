# Arch Guards Optimization — Complete Spec

## Status
- **Phase 1 (Delete 18 duplicates)**: DONE ✓ — merged to main
- **Phase 2 (Keep structural guards)**: DONE ✓ — untouched
- **Phase 3 (Convert guards to Roslyn)**: TODO

## Phase 3: Convert ~30 arch guards to Roslyn analyzers

### Why
- Arch guards use regex on file content → false positives, fragile, slow
- Roslyn analyzers understand the AST → zero false positives, instant (compile-time), IDE squiggles

### Guards to convert (highest value first)

Each of these is currently a regex scan in `PlatformGuardTests.cs`. Convert each to a `DiagnosticAnalyzer` in `src/Analyzers/Haworks.Architecture.Analyzers/Rules/`.

#### Priority 1: Financial safety
1. **No_float_or_double_for_money** → Already `HWK029` ✓
2. **No_SaveChanges_before_publish_without_transaction** → New `HWK051`
3. **Financial_decimal_properties_have_explicit_column_type_in_DbContext** → Already `HWK003` ✓
4. **Consumer_handling_financial_events_checks_idempotency** → New `HWK052`
5. **Ledger_and_financial_services_wrap_writes_in_transactions** → New `HWK053`

#### Priority 2: Security
6. **No_userId_from_request_body_in_state_changing_endpoints** → New `HWK054` (IDOR prevention)
7. **No_unvalidated_user_URLs_in_HTTP_calls** → New `HWK055` (SSRF prevention)
8. **No_AllowAnyOrigin_with_AllowCredentials** → New `HWK056`
9. **No_CORS_wildcard_in_production** → New `HWK057`
10. **No_secrets_in_source_code** → New `HWK058`

#### Priority 3: Code quality
11. **No_throw_ex_loses_stack_trace** → New `HWK059` (use `throw` not `throw ex`)
12. **No_Console_Write_in_production_code** → New `HWK060`
13. **No_TODO_comments_in_source_code** → New `HWK061`
14. **No_Guid_Empty_as_real_identifier** → New `HWK062`
15. **No_DbContext_registered_as_singleton** → New `HWK063`

#### Priority 4: EF Core patterns
16. **No_navigation_property_access_inside_loops_without_Include** → New `HWK064` (N+1)
17. **No_tracked_queries_in_read_only_handlers** → New `HWK065`
18. **EF_string_properties_have_MaxLength** → New `HWK066`
19. **No_Entity_records_used_with_EF_Core** → New `HWK067`

### How to create a Roslyn analyzer

Follow the pattern in existing analyzers. Example — `HWK019_NoHardcodedLocalhostAnalyzer.cs`:

```csharp
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK059_NoThrowExAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoThrowEx);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeThrow, SyntaxKind.ThrowStatement);
    }

    private static void AnalyzeThrow(SyntaxNodeAnalysisContext context)
    {
        var throwStatement = (ThrowStatementSyntax)context.Node;
        if (throwStatement.Expression is IdentifierNameSyntax id &&
            id.Identifier.Text == "ex")
        {
            // Check if we're inside a catch block
            if (throwStatement.Ancestors().Any(a => a is CatchClauseSyntax))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.NoThrowEx,
                    throwStatement.GetLocation(),
                    id.Identifier.Text));
            }
        }
    }
}
```

### Steps for each conversion
1. Add `DiagnosticDescriptor` to `Diagnostics.cs`
2. Create analyzer file in `Rules/`
3. Add test in `tests/Analyzers/Analyzers.Unit/`
4. Delete the corresponding arch guard test method from `PlatformGuardTests.cs`
5. Build + verify: `dotnet build src/Analyzers/ && dotnet test tests/Platform.ArchitecturalGuards/`

### What stays as arch guards (cannot be Roslyn analyzers)
- File existence checks (README, Dockerfile, fly.toml, migrations)
- Cross-project reference checks (Domain→Infra — needs solution-level analysis)
- Saga structural checks (SetCompletedWhenFinalized — needs multi-file analysis)
- Test factory pattern checks
- CI/deployment config checks
