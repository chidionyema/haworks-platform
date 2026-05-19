using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK078: Detects the TOCTOU pattern on ConcurrentDictionary:
///   if (!dict.ContainsKey(key)) dict[key] = value;
/// This is a race condition. Use GetOrAdd, TryAdd, or AddOrUpdate instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK078_NoConcurrentDictionaryCheckThenActAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] CheckMethods = { "ContainsKey", "TryGetValue" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoConcurrentDictionaryCheckThenAct);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeIfStatement, SyntaxKind.IfStatement);
    }

    private static void AnalyzeIfStatement(SyntaxNodeAnalysisContext context)
    {
        var ifStatement = (IfStatementSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // Find ContainsKey/TryGetValue calls in the condition
        var conditionInvocations = ifStatement.Condition.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>();

        foreach (var condInvocation in conditionInvocations)
        {
            if (semanticModel.GetSymbolInfo(condInvocation, context.CancellationToken).Symbol
                is not IMethodSymbol checkMethod) continue;

            if (!CheckMethods.Contains(checkMethod.Name)) continue;
            if (!IsConcurrentDictionary(checkMethod.ContainingType)) continue;

            // Get the variable being checked (the dictionary instance)
            var dictExpr = GetReceiverExpression(condInvocation);
            if (dictExpr == null) continue;
            var dictText = dictExpr.ToString();

            // Check if the if-body or else-body contains indexer assignment on the same dictionary
            var bodyToCheck = ifStatement.Statement;
            if (HasIndexerAssignment(bodyToCheck, dictText, semanticModel, context.CancellationToken))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.NoConcurrentDictionaryCheckThenAct,
                    condInvocation.GetLocation(),
                    checkMethod.Name));
                return;
            }

            // Also check else branch (for negated conditions like !ContainsKey)
            if (ifStatement.Else?.Statement != null &&
                HasIndexerAssignment(ifStatement.Else.Statement, dictText, semanticModel, context.CancellationToken))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.NoConcurrentDictionaryCheckThenAct,
                    condInvocation.GetLocation(),
                    checkMethod.Name));
                return;
            }
        }
    }

    private static bool IsConcurrentDictionary(INamedTypeSymbol type)
    {
        if (type == null) return false;
        return type.Name == "ConcurrentDictionary" &&
               type.ContainingNamespace?.ToDisplayString() == "System.Collections.Concurrent";
    }

    private static ExpressionSyntax? GetReceiverExpression(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            return memberAccess.Expression;
        return null;
    }

    private static bool HasIndexerAssignment(
        StatementSyntax body, string dictText, SemanticModel model, System.Threading.CancellationToken ct)
    {
        // Look for dict[key] = value patterns
        var assignments = body.DescendantNodes().OfType<AssignmentExpressionSyntax>();
        foreach (var assignment in assignments)
        {
            if (assignment.Left is ElementAccessExpressionSyntax elementAccess &&
                elementAccess.Expression.ToString() == dictText)
            {
                return true;
            }
        }

        // Also look for dict.Add(key, value) — though Add doesn't exist on ConcurrentDictionary,
        // developers sometimes cast or use indexer via extension methods
        return false;
    }
}
