using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK083: Detects classes that implement IRequest (MediatR command) and end in "Command"
/// but have no corresponding AbstractValidator in the same compilation.
/// Query commands (Get*, List*, Search*, Find*) are excluded.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK083_NoCommandWithoutValidatorAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] QueryPrefixes =
    {
        "Get", "List", "Search", "Find", "Validate", "Check", "Count", "Exists", "Calculate"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoCommandWithoutValidator);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var validatorTypes = new HashSet<string>();
        var commandTypes = new List<(INamedTypeSymbol Symbol, Location Location)>();

        context.RegisterSymbolAction(ctx =>
        {
            var namedType = (INamedTypeSymbol)ctx.Symbol;

            // Collect validators
            var baseType = namedType.BaseType;
            while (baseType != null)
            {
                if (baseType.Name == "AbstractValidator" && baseType.IsGenericType)
                {
                    var typeArg = baseType.TypeArguments.FirstOrDefault();
                    if (typeArg != null)
                        validatorTypes.Add(typeArg.Name);
                }
                baseType = baseType.BaseType;
            }

            // Collect commands
            var name = namedType.Name;
            if (!name.EndsWith("Command", System.StringComparison.Ordinal)) return;
            if (QueryPrefixes.Any(p => name.StartsWith(p, System.StringComparison.Ordinal))) return;

            bool implementsIRequest = namedType.AllInterfaces.Any(i =>
                i.Name == "IRequest" || i.Name == "ICommand");
            if (!implementsIRequest) return;

            var location = namedType.Locations.FirstOrDefault();
            if (location != null)
            {
                lock (commandTypes)
                {
                    commandTypes.Add((namedType, location));
                }
            }
        }, SymbolKind.NamedType);

        context.RegisterCompilationEndAction(compilationCtx =>
        {
            foreach (var (command, location) in commandTypes)
            {
                if (!validatorTypes.Contains(command.Name))
                {
                    compilationCtx.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.NoCommandWithoutValidator,
                        location,
                        command.Name));
                }
            }
        });
    }
}
