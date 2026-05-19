using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK073_NoSideEffectsInPollyRetryAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.SideEffectsInPollyRetry);

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
            methodSymbol.Name != "ExecuteAsync" ||
            methodSymbol.ContainingNamespace?.ToDisplayString().StartsWith("Polly", System.StringComparison.Ordinal) != true)
        {
            return;
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Expression is not AnonymousFunctionExpressionSyntax lambda) continue;

            var innerInvocations = lambda.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var innerCall in innerInvocations)
            {
                if (semanticModel.GetSymbolInfo(innerCall, context.CancellationToken).Symbol is not IMethodSymbol innerMethod) continue;

                var innerName = innerMethod.Name;
                var innerNamespace = innerMethod.ContainingType?.ContainingNamespace?.ToDisplayString() ?? "";

                bool isDbWrite = innerName == "SaveChangesAsync" || innerName == "SaveChanges";
                bool isPublish = innerName.Contains("Publish") || innerName.Contains("Send");
                bool isMassTransitOrMediatR = innerNamespace.StartsWith("MassTransit", System.StringComparison.Ordinal) || innerNamespace.StartsWith("MediatR", System.StringComparison.Ordinal);

                if (isDbWrite || (isPublish && isMassTransitOrMediatR))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.SideEffectsInPollyRetry,
                        innerCall.GetLocation(),
                        innerMethod.Name));
                }
            }
        }
    }
}
