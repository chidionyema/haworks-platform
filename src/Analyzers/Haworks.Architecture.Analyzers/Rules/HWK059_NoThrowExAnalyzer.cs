using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK059_NoThrowExAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoThrowOriginalException);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeThrow, SyntaxKind.ThrowStatement);
    }

    private static void AnalyzeThrow(SyntaxNodeAnalysisContext context)
    {
        var throwStatement = (ThrowStatementSyntax)context.Node;
        if (throwStatement.Expression is not IdentifierNameSyntax id)
            return;

        // Check if identifier refers to the catch variable
        var catchClause = throwStatement.Ancestors().OfType<CatchClauseSyntax>().FirstOrDefault();
        if (catchClause == null)
            return;

        var catchVarName = catchClause.Declaration?.Identifier.Text;
        if (catchVarName != null && id.Identifier.Text == catchVarName)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.NoThrowOriginalException, throwStatement.GetLocation(), id.Identifier.Text));
        }
    }
}
