using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK080: Detects SaveChangesAsync/SaveChanges called inside foreach/for/while loops.
/// Each call is a round-trip to the database. Batch mutations and save once after the loop.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK080_NoSaveChangesInLoopAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoSaveChangesInLoop);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method) return;

        if (method.Name != "SaveChangesAsync" && method.Name != "SaveChanges") return;

        var containingNamespace = method.ContainingType?.ContainingNamespace?.ToDisplayString() ?? "";
        if (!containingNamespace.StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal)) return;

        // Walk ancestors to find enclosing loop
        foreach (var ancestor in invocation.Ancestors())
        {
            switch (ancestor)
            {
                case ForEachStatementSyntax:
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.NoSaveChangesInLoop, invocation.GetLocation(), "foreach"));
                    return;
                case ForStatementSyntax:
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.NoSaveChangesInLoop, invocation.GetLocation(), "for"));
                    return;
                case WhileStatementSyntax:
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.NoSaveChangesInLoop, invocation.GetLocation(), "while"));
                    return;
                case DoStatementSyntax:
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.NoSaveChangesInLoop, invocation.GetLocation(), "do-while"));
                    return;
                // Stop at method boundary
                case MethodDeclarationSyntax:
                case LocalFunctionStatementSyntax:
                case AnonymousFunctionExpressionSyntax:
                    return;
            }
        }
    }
}
