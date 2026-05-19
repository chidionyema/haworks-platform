using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK057: CORS wildcard "*" in WithOrigins allows any domain.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK057_NoCorsWildcardAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoCorsWildcard);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax ma)
            return;

        var methodName = ma.Name.Identifier.Text;
        if (methodName is not ("WithOrigins" or "AllowAnyOrigin"))
            return;

        if (methodName == "AllowAnyOrigin")
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.NoCorsWildcard, invocation.GetLocation()));
            return;
        }

        // Check WithOrigins("*")
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (arg.Expression is LiteralExpressionSyntax literal &&
                literal.Token.ValueText == "*")
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.NoCorsWildcard, arg.GetLocation()));
                return;
            }
        }
    }
}
