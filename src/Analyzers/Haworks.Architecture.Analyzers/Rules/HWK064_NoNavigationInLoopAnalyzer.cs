using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK064: Accessing navigation properties inside loops causes N+1 queries.
/// Detects member access on entity-like objects inside for/foreach/while loops.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK064_NoNavigationInLoopAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] NavigationSuffixes =
        { "Items", "Details", "Entries", "History", "Lines", "Children",
          "Orders", "Payments", "Products", "Addresses", "Roles" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoNavigationInLoop);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var ma = (MemberAccessExpressionSyntax)context.Node;
        var propName = ma.Name.Identifier.Text;

        // Check if property name looks like a navigation collection
        if (!NavigationSuffixes.Any(s => propName.EndsWith(s, System.StringComparison.Ordinal)))
            return;

        // Must be inside a loop
        bool insideLoop = ma.Ancestors().Any(a =>
            a is ForEachStatementSyntax or ForStatementSyntax or
                WhileStatementSyntax or DoStatementSyntax);
        if (!insideLoop)
            return;

        // Skip if we're in a test file
        var filePath = context.Node.SyntaxTree.FilePath ?? "";
        if (filePath.Contains("/tests/") || filePath.Contains("/Tests/") ||
            filePath.Contains("\\tests\\") || filePath.Contains("\\Tests\\"))
            return;

        // Skip if the method contains Include for this property
        var method = ma.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method != null)
        {
            var methodText = method.ToString();
            if (methodText.Contains($"Include(") && methodText.Contains(propName))
                return;
            if (methodText.Contains($".{propName} ="))
                return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.NoNavigationInLoop,
            ma.Name.GetLocation(),
            propName));
    }
}
