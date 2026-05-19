using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK062_NoGuidEmptyAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoGuidEmpty);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var ma = (MemberAccessExpressionSyntax)context.Node;
        if (ma.Name.Identifier.Text != "Empty")
            return;

        var receiver = ma.Expression.ToString();
        if (receiver is not ("Guid" or "System.Guid"))
            return;

        // Allow in comparisons (== Guid.Empty, != Guid.Empty) — that's a valid check
        if (ma.Parent is BinaryExpressionSyntax bin &&
            bin.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression)
            return;

        // Allow in arguments to Equals method
        if (ma.Parent is ArgumentSyntax arg &&
            arg.Parent?.Parent is InvocationExpressionSyntax inv &&
            inv.Expression.ToString().EndsWith("Equals", System.StringComparison.Ordinal))
            return;

        // Allow in test code
        var filePath = context.Node.SyntaxTree.FilePath ?? "";
        if (filePath.Contains("/tests/") || filePath.Contains("/Tests/") ||
            filePath.Contains("\\tests\\") || filePath.Contains("\\Tests\\"))
            return;

        // Get context name for message
        var assignment = ma.FirstAncestorOrSelf<AssignmentExpressionSyntax>();
        var varName = assignment?.Left.ToString() ?? "identifier";

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.NoGuidEmpty, ma.GetLocation(), varName));
    }
}
