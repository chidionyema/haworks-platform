using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK072_NoSwallowedExceptionInPollyAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.SwallowedPollyException);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        if (semanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol methodSymbol ||
            methodSymbol.Name != "ExecuteAsync")
        {
            return;
        }

        var containingNamespace = methodSymbol.ContainingNamespace?.ToDisplayString();
        if (containingNamespace == null || !containingNamespace.StartsWith("Polly", System.StringComparison.Ordinal))
        {
            return;
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Expression is not AnonymousFunctionExpressionSyntax lambda) continue;

            var catchBlocks = lambda.DescendantNodes().OfType<CatchClauseSyntax>();
            foreach (var catchBlock in catchBlocks)
            {
                var hasThrow = catchBlock.Block.DescendantNodes().OfType<ThrowStatementSyntax>().Any();
                var hasExpressionThrow = catchBlock.Block.DescendantNodes().OfType<ThrowExpressionSyntax>().Any();

                if (!hasThrow && !hasExpressionThrow)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.SwallowedPollyException,
                        catchBlock.CatchKeyword.GetLocation()));
                }
            }
        }
    }
}
