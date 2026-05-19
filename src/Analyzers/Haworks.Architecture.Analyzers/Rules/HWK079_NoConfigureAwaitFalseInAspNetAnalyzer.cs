using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK079: Flags ConfigureAwait(false) in ASP.NET Core projects.
/// ASP.NET Core has no SynchronizationContext, so ConfigureAwait(false) is a no-op
/// that adds noise and can mask bugs when code is refactored into library projects.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK079_NoConfigureAwaitFalseInAspNetAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoConfigureAwaitFalseInAspNet);

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

        if (method.Name != "ConfigureAwait") return;
        if (method.ContainingNamespace?.ToDisplayString() != "System.Runtime.CompilerServices"
            && method.ContainingType?.Name != "Task"
            && method.ContainingType?.Name != "ValueTask") return;

        // Check the argument is literally `false`
        if (invocation.ArgumentList.Arguments.Count == 0) return;
        var arg = invocation.ArgumentList.Arguments[0].Expression;
        if (arg is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.FalseLiteralExpression))
            return;

        // Only flag in ASP.NET projects (check for Controller/ControllerBase types or assembly refs)
        var compilation = context.SemanticModel.Compilation;
        bool isAspNetProject = compilation.ReferencedAssemblyNames
            .Any(a => a.Name.StartsWith("Microsoft.AspNetCore", System.StringComparison.Ordinal));

        // Also check if ASP.NET types exist in compilation (handles source stubs in tests)
        if (!isAspNetProject)
        {
            isAspNetProject = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.Controller") != null ||
                              compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.IMiddleware") != null;
        }

        if (!isAspNetProject) return;

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.NoConfigureAwaitFalseInAspNet,
            invocation.GetLocation()));
    }
}
