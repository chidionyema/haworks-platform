using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK082: Detects direct HttpClient method calls (SendAsync, GetAsync, PostAsync, etc.)
/// that are NOT inside a Polly ExecuteAsync delegate. External HTTP calls should be wrapped
/// in resilience policies (retry, circuit-breaker, timeout).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK082_NoHttpCallWithoutResiliencePolicyAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] HttpMethods =
    {
        "SendAsync", "GetAsync", "PostAsync", "PutAsync", "DeleteAsync",
        "PatchAsync", "GetStringAsync", "GetStreamAsync", "GetByteArrayAsync"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoHttpCallWithoutResiliencePolicy);

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

        if (!HttpMethods.Contains(method.Name)) return;

        var typeName = method.ContainingType?.Name ?? "";
        if (typeName != "HttpClient") return;

        var typeNs = method.ContainingType?.ContainingNamespace?.ToDisplayString() ?? "";
        if (!typeNs.StartsWith("System.Net.Http")) return;

        // Check if this call is inside a Polly ExecuteAsync lambda
        if (IsInsidePollyExecute(invocation, context.SemanticModel, context.CancellationToken))
            return;

        // Allow in test classes
        var classDecl = invocation.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDecl?.Identifier.Text.EndsWith("Tests") == true ||
            classDecl?.Identifier.Text.EndsWith("Test") == true)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.NoHttpCallWithoutResiliencePolicy,
            invocation.GetLocation(),
            method.Name));
    }

    private static bool IsInsidePollyExecute(
        SyntaxNode node, SemanticModel model, System.Threading.CancellationToken ct)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is AnonymousFunctionExpressionSyntax)
            {
                if (ancestor.Parent is ArgumentSyntax { Parent: { Parent: InvocationExpressionSyntax parentInvocation } })
                {
                    if (model.GetSymbolInfo(parentInvocation, ct).Symbol is IMethodSymbol parentMethod &&
                        parentMethod.Name == "ExecuteAsync" &&
                        parentMethod.ContainingNamespace?.ToDisplayString().StartsWith("Polly") == true)
                    {
                        return true;
                    }
                }
            }

            if (ancestor is MethodDeclarationSyntax)
                break;
        }
        return false;
    }
}
