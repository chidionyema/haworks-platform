using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK074_NoManualConsumerTransactionAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.CompetingConsumerTransaction);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeClassDeclaration, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeClassDeclaration(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration, context.CancellationToken);

        if (classSymbol == null) return;

        bool implementsConsumer = classSymbol.AllInterfaces.Any(i =>
            i.ContainingNamespace?.ToDisplayString().StartsWith("MassTransit", System.StringComparison.Ordinal) == true &&
            i.Name == "IConsumer");

        if (!implementsConsumer) return;

        var invocations = classDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>();
        foreach (var invocation in invocations)
        {
            if (semanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol methodSymbol) continue;

            if (methodSymbol.Name == "BeginTransactionAsync" || methodSymbol.Name == "BeginTransaction")
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.CompetingConsumerTransaction,
                    invocation.GetLocation(),
                    methodSymbol.Name));
            }
        }
    }
}
