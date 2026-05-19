using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK061_NoTodoCommentsAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoTodoComments);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        var filePath = context.Tree.FilePath ?? "";
        if (filePath.Contains("/tests/") || filePath.Contains("/Tests/") ||
            filePath.Contains("\\tests\\") || filePath.Contains("\\Tests\\"))
            return;

        var root = context.Tree.GetRoot(context.CancellationToken);
        foreach (var trivia in root.DescendantTrivia())
        {
            if (trivia.Kind() is not (SyntaxKind.SingleLineCommentTrivia or
                SyntaxKind.MultiLineCommentTrivia or
                SyntaxKind.SingleLineDocumentationCommentTrivia))
                continue;

            var text = trivia.ToString();
            if (text.IndexOf("TODO", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("HACK", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("FIXME", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.NoTodoComments, trivia.GetLocation()));
            }
        }
    }
}
