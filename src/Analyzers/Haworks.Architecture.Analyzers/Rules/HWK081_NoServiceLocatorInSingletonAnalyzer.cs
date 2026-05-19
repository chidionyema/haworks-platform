using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK081: Detects IServiceProvider.GetService/GetRequiredService usage inside
/// class methods (service locator anti-pattern). Constructor injection should be
/// preferred. Exception: factory delegates and DI registration lambdas are allowed.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK081_NoServiceLocatorInSingletonAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] ServiceLocatorMethods =
    {
        "GetService", "GetRequiredService", "CreateScope"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoServiceLocatorInSingleton);

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

        if (!ServiceLocatorMethods.Contains(method.Name)) return;

        var containingType = method.ContainingType;
        if (containingType == null) return;

        // Must be on IServiceProvider or IServiceScopeFactory
        var typeName = containingType.Name;
        var ns = containingType.ContainingNamespace?.ToDisplayString() ?? "";
        bool isServiceProvider = (typeName == "IServiceProvider" || typeName == "ServiceProvider") &&
                                 ns.StartsWith("System");
        bool isServiceScopeFactory = typeName == "IServiceScopeFactory" || typeName == "IServiceScope";
        bool isExtensions = typeName == "ServiceProviderServiceExtensions";

        if (!isServiceProvider && !isServiceScopeFactory && !isExtensions) return;

        // Allow in DI registration lambdas (inside AddScoped/AddSingleton/AddTransient/Configure calls)
        if (IsInsideDiRegistrationLambda(invocation)) return;

        // Allow in constructors (rare but valid for factory patterns)
        if (invocation.Ancestors().OfType<ConstructorDeclarationSyntax>().Any()) return;

        // Allow CreateScope — it's the correct escape hatch
        if (method.Name == "CreateScope") return;

        var enclosingMethod = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var methodName = enclosingMethod?.Identifier.Text ?? "unknown";

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.NoServiceLocatorInSingleton,
            invocation.GetLocation(),
            $"{method.Name} in {methodName}"));
    }

    private static bool IsInsideDiRegistrationLambda(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is AnonymousFunctionExpressionSyntax)
            {
                // Check if the lambda is an argument to a DI method
                if (ancestor.Parent is ArgumentSyntax { Parent: { Parent: InvocationExpressionSyntax parentInvocation } })
                {
                    var parentMethodName = parentInvocation.Expression.ToString();
                    if (parentMethodName.Contains("AddScoped") ||
                        parentMethodName.Contains("AddSingleton") ||
                        parentMethodName.Contains("AddTransient") ||
                        parentMethodName.Contains("AddHostedService") ||
                        parentMethodName.Contains("Configure"))
                        return true;
                }
            }

            if (ancestor is MethodDeclarationSyntax)
                break;
        }
        return false;
    }
}
