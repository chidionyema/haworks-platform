using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK077: Detects ToListAsync/ToList/ToArrayAsync calls on IQueryable without
/// a preceding Take/First/Single call in the LINQ chain. Unbounded materialization
/// can load millions of rows into memory.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK077_NoUnboundedToListAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] MaterializeMethods =
    {
        "ToListAsync", "ToList", "ToArrayAsync", "ToArray"
    };

    private static readonly string[] BoundingMethods =
    {
        "Take", "First", "FirstOrDefault", "FirstAsync", "FirstOrDefaultAsync",
        "Single", "SingleOrDefault", "SingleAsync", "SingleOrDefaultAsync",
        "Find", "FindAsync", "CountAsync", "Count", "AnyAsync", "Any",
        "MaxAsync", "MinAsync", "SumAsync", "AverageAsync"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoUnboundedToList);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method) return;

        if (!MaterializeMethods.Contains(method.Name)) return;

        // Check if the receiver type is IQueryable<T> or DbSet<T>
        var receiverType = GetReceiverType(invocation, context.SemanticModel, context.CancellationToken);
        if (receiverType == null) return;

        bool isQueryable = receiverType.Name == "IQueryable" ||
                           receiverType.AllInterfaces.Any(i => i.Name == "IQueryable") ||
                           receiverType.Name == "DbSet" ||
                           receiverType.BaseType?.Name == "DbSet";

        if (!isQueryable) return;

        // Walk the invocation chain backwards to check for bounding methods
        if (HasBoundingMethodInChain(invocation, context.SemanticModel, context.CancellationToken))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.NoUnboundedToList,
            invocation.GetLocation(),
            method.Name));
    }

    private static ITypeSymbol? GetReceiverType(
        InvocationExpressionSyntax invocation, SemanticModel model, System.Threading.CancellationToken ct)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return model.GetTypeInfo(memberAccess.Expression, ct).Type;
        }
        return null;
    }

    private static bool HasBoundingMethodInChain(
        InvocationExpressionSyntax invocation, SemanticModel model, System.Threading.CancellationToken ct)
    {
        // Walk up the member access chain: _db.Users.Where(...).Take(10).ToListAsync()
        var current = invocation.Expression as MemberAccessExpressionSyntax;
        while (current != null)
        {
            if (current.Expression is InvocationExpressionSyntax chainedCall)
            {
                var chainedMethodSymbol = model.GetSymbolInfo(chainedCall, ct).Symbol as IMethodSymbol;
                if (chainedMethodSymbol != null && BoundingMethods.Contains(chainedMethodSymbol.Name))
                {
                    return true;
                }

                current = chainedCall.Expression as MemberAccessExpressionSyntax;
            }
            else
            {
                break;
            }
        }

        return false;
    }
}
