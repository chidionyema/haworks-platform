using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK063_NoDbContextSingletonAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoDbContextSingleton);

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
        if (methodName is not ("AddSingleton"))
            return;

        // Check if type argument contains "DbContext"
        if (ma.Name is GenericNameSyntax generic)
        {
            foreach (var typeArg in generic.TypeArgumentList.Arguments)
            {
                var typeName = typeArg.ToString();
                if (typeName.Contains("DbContext"))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.NoDbContextSingleton, invocation.GetLocation(), typeName));
                    return;
                }
            }
        }

        // Check regular arguments for DbContext type
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            var argText = arg.ToString();
            if (argText.Contains("DbContext"))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.NoDbContextSingleton, invocation.GetLocation(), argText));
                return;
            }
        }
    }
}
