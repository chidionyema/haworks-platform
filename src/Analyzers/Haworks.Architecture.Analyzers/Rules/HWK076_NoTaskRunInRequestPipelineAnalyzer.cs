using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK076: Detects Task.Run usage inside Controller or Middleware classes.
/// Fire-and-forget loses request context (correlation ID, user claims, CancellationToken).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK076_NoTaskRunInRequestPipelineAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoTaskRunInRequestPipeline);

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

        if (method.ContainingType?.Name != "Task" || method.Name != "Run") return;
        if (method.ContainingNamespace?.ToDisplayString() != "System.Threading.Tasks") return;

        var classDecl = invocation.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDecl == null) return;

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDecl, context.CancellationToken);
        if (classSymbol == null) return;

        if (IsRequestPipelineClass(classSymbol))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.NoTaskRunInRequestPipeline,
                invocation.GetLocation(),
                classSymbol.Name));
        }
    }

    private static bool IsRequestPipelineClass(INamedTypeSymbol classSymbol)
    {
        // Check base types for Controller/ControllerBase
        var current = classSymbol;
        while (current != null)
        {
            var name = current.Name;
            if (name == "Controller" || name == "ControllerBase" || name == "ApiController")
                return true;
            current = current.BaseType;
        }

        // Check interfaces for IMiddleware, IActionFilter, IAsyncActionFilter
        return classSymbol.AllInterfaces.Any(i =>
            i.Name == "IMiddleware" ||
            i.Name == "IActionFilter" ||
            i.Name == "IAsyncActionFilter" ||
            i.Name == "IEndpointFilter");
    }
}
