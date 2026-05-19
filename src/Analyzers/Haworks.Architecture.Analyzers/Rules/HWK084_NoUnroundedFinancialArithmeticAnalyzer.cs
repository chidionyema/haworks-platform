using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK084: Detects decimal multiplication or division in financial namespaces
/// (*.Ledger.*, *.Pricing.*, *.Payments.*) where the result is not immediately
/// wrapped in Math.Round. Unrounded financial arithmetic causes penny-drift.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK084_NoUnroundedFinancialArithmeticAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] FinancialNamespaceSegments =
    {
        "Ledger", "Pricing", "Payments", "Payouts", "Billing", "Accounting"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoUnroundedFinancialArithmetic);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeBinaryExpression,
            SyntaxKind.MultiplyExpression, SyntaxKind.DivideExpression);
    }

    private static void AnalyzeBinaryExpression(SyntaxNodeAnalysisContext context)
    {
        var binary = (BinaryExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // Skip inner expressions when parent is also a multiply/divide — only flag the outermost
        if (binary.Parent is BinaryExpressionSyntax parentBinary &&
            (parentBinary.IsKind(SyntaxKind.MultiplyExpression) || parentBinary.IsKind(SyntaxKind.DivideExpression)))
            return;

        // Check result type is decimal
        var typeInfo = semanticModel.GetTypeInfo(binary, context.CancellationToken);
        if (typeInfo.Type?.SpecialType != SpecialType.System_Decimal) return;

        // Check we're in a financial namespace
        var containingSymbol = semanticModel.GetEnclosingSymbol(binary.SpanStart, context.CancellationToken);
        if (containingSymbol == null) return;

        var ns = containingSymbol.ContainingNamespace?.ToDisplayString() ?? "";
        if (!FinancialNamespaceSegments.Any(seg => ns.Contains(seg))) return;

        // Check if the parent is Math.Round
        if (IsWrappedInMathRound(binary)) return;

        // Check if the result is assigned to a variable that's then rounded on the next line
        if (IsRoundedInAssignmentContext(binary)) return;

        // Get a meaningful name for the expression
        var exprText = binary.Left.ToString();
        if (exprText.Length > 30) exprText = exprText.Substring(0, 27) + "...";

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.NoUnroundedFinancialArithmetic,
            binary.GetLocation(),
            exprText));
    }

    private static bool IsWrappedInMathRound(SyntaxNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current is InvocationExpressionSyntax invocation)
            {
                var methodName = invocation.Expression.ToString();
                if (methodName == "Math.Round" || methodName == "decimal.Round")
                    return true;
            }

            // Don't walk past statement boundaries
            if (current is StatementSyntax) break;

            // Parenthesized expressions are fine to traverse
            if (current is ParenthesizedExpressionSyntax ||
                current is ArgumentSyntax ||
                current is ArgumentListSyntax ||
                current is BinaryExpressionSyntax ||
                current is CastExpressionSyntax)
            {
                current = current.Parent;
                continue;
            }

            break;
        }
        return false;
    }

    private static bool IsRoundedInAssignmentContext(BinaryExpressionSyntax binary)
    {
        // Check: var x = a * b; ... Math.Round(x, ...)
        // This is a heuristic — we check if the assignment target is used in Math.Round
        // within the same block
        if (binary.Parent is EqualsValueClauseSyntax equalsClause &&
            equalsClause.Parent is VariableDeclaratorSyntax declarator)
        {
            var varName = declarator.Identifier.Text;
            var containingBlock = binary.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
            if (containingBlock != null)
            {
                var roundCalls = containingBlock.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Where(i => i.Expression.ToString().Contains("Math.Round") ||
                                i.Expression.ToString().Contains("decimal.Round"));

                foreach (var roundCall in roundCalls)
                {
                    if (roundCall.ArgumentList.Arguments.Any(a => a.Expression.ToString() == varName))
                        return true;
                }
            }
        }
        return false;
    }
}
