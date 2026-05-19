using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK051: SaveChangesAsync then PublishAsync without a transaction means
/// the DB change is committed but the event may never be published.
/// Use Outbox (Publish then SaveChanges) or wrap both in a transaction.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK051_NoSaveChangesBeforePublishWithoutTransactionAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoSaveChangesBeforePublishWithoutTransaction);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (method.Body == null && method.ExpressionBody == null)
            return;

        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();

        bool hasSaveChanges = false;
        bool hasTransaction = false;
        InvocationExpressionSyntax? saveChangesNode = null;

        foreach (var inv in invocations)
        {
            var name = GetMethodName(inv);

            if (name == "BeginTransactionAsync" || name == "BeginTransaction")
                hasTransaction = true;

            if (name == "SaveChangesAsync" || name == "SaveChanges")
            {
                hasSaveChanges = true;
                saveChangesNode ??= inv;
            }
        }

        if (!hasSaveChanges || hasTransaction)
            return;

        // Check if SaveChanges appears before Publish in statement order
        foreach (var inv in invocations)
        {
            var name = GetMethodName(inv);
            if (name is "Publish" or "PublishAsync")
            {
                if (saveChangesNode != null &&
                    saveChangesNode.SpanStart < inv.SpanStart)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.NoSaveChangesBeforePublishWithoutTransaction,
                        saveChangesNode.GetLocation()));
                    return;
                }
            }
        }
    }

    private static string GetMethodName(InvocationExpressionSyntax inv)
    {
        return inv.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => ""
        };
    }
}
