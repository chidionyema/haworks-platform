using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

        // First pass: collect all AbstractValidator<T> type arguments
        context.RegisterSymbolAction(ctx =>
        {
            var namedType = (INamedTypeSymbol)ctx.Symbol;
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
        }, SymbolKind.NamedType);

        // Second pass: check commands
        context.RegisterCompilationEndAction(compilationCtx =>
        {
            foreach (var tree in compilationCtx.Compilation.SyntaxTrees)
            {
                var model = compilationCtx.Compilation.GetSemanticModel(tree);
                var root = tree.GetRoot(compilationCtx.CancellationToken);

                foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    var symbol = model.GetDeclaredSymbol(typeDecl, compilationCtx.CancellationToken);
                    if (symbol is not INamedTypeSymbol namedType) continue;

                    var name = namedType.Name;
                    if (!name.EndsWith("Command")) continue;
                    if (QueryPrefixes.Any(p => name.StartsWith(p))) continue;

                    // Must implement IRequest (MediatR)
                    bool implementsIRequest = namedType.AllInterfaces.Any(i =>
                        i.Name == "IRequest" || i.Name == "ICommand");
                    if (!implementsIRequest) continue;

                    if (!validatorTypes.Contains(name))
                    {
                        compilationCtx.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.NoCommandWithoutValidator,
                            typeDecl.Identifier.GetLocation(),
                            name));
                    }
                }
            }
        });
    }
}
