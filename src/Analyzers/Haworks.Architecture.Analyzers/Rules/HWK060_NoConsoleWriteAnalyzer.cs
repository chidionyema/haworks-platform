using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK060_NoConsoleWriteAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoConsoleWrite);

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
        if (methodName is not ("Write" or "WriteLine"))
            return;

        var receiver = ma.Expression.ToString();
        if (receiver is "Console" or "System.Console")
        {
            var filePath = context.Node.SyntaxTree.FilePath ?? "";
            if (filePath.Contains("/tests/") || filePath.Contains("/Tests/") ||
                filePath.Contains("\\tests\\") || filePath.Contains("\\Tests\\"))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.NoConsoleWrite, invocation.GetLocation(), $"Console.{methodName}"));
        }
    }
}
