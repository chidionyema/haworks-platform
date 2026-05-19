using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK009: Detects external HTTP/API calls made while a database transaction is held open.
/// The Three-Phase pattern requires external I/O to happen OUTSIDE any DB transaction scope.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK009_NoExternalIoInsideTransactionAnalyzer : DiagnosticAnalyzer
{
    private static readonly HashSet<string> TransactionStartMethods = new()
    {
        "BeginTransactionAsync",
        "BeginTransaction"
    };

    private static readonly HashSet<string> ExternalIoMethods = new()
    {
        "SendAsync", "GetAsync", "PostAsync", "PutAsync", "DeleteAsync",
        "PatchAsync", "GetStringAsync", "GetStreamAsync", "GetByteArrayAsync"
    };

    private static readonly HashSet<string> ExternalIoContainingTypes = new()
    {
        "HttpClient", "IHttpClientFactory"
    };

    private static readonly HashSet<string> ExternalIoNamespacePrefixes = new()
    {
        "System.Net.Http",
        "Stripe",
        "PayPal",
        "Amazon.S3",
        "Nest",
        "Elasticsearch"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoExternalIoInsideTransaction);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (method.Body == null && method.ExpressionBody == null) return;

        var semanticModel = context.SemanticModel;
        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();

        // Find all transaction-start invocations and their scopes (scope node + tx start position)
        var txScopes = new List<(SyntaxNode Scope, int TxStart)>();
        foreach (var invocation in invocations)
        {
            if (semanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is not IMethodSymbol methodSymbol) continue;

            if (!TransactionStartMethods.Contains(methodSymbol.Name)) continue;

            var usingScope = FindEnclosingUsingScope(invocation);
            if (usingScope != null)
            {
                // Only flag invocations that appear AFTER the transaction start
                txScopes.Add((usingScope, invocation.SpanStart));
            }
            else
            {
                txScopes.Add((method.Body ?? (SyntaxNode)method, invocation.SpanStart));
            }
        }

        if (txScopes.Count == 0) return;

        // Check every invocation inside a transaction scope for external I/O
        foreach (var invocation in invocations)
        {
            // Must be within scope AND after the transaction start position
            if (!txScopes.Any(tx => tx.Scope.Span.Contains(invocation.Span)
                                    && invocation.SpanStart > tx.TxStart)) continue;

            if (semanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is not IMethodSymbol calledMethod) continue;

            if (IsExternalIoCall(calledMethod))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.NoExternalIoInsideTransaction,
                    invocation.GetLocation(),
                    calledMethod.Name));
            }
        }
    }

    private static bool IsExternalIoCall(IMethodSymbol method)
    {
        var typeName = method.ContainingType?.Name ?? "";
        var typeNamespace = method.ContainingType?.ContainingNamespace?.ToDisplayString() ?? "";

        // Direct HttpClient method calls
        if (ExternalIoContainingTypes.Contains(typeName) && ExternalIoMethods.Contains(method.Name))
            return true;

        // Known external SDK namespaces — any async method is likely I/O
        if (ExternalIoNamespacePrefixes.Any(prefix => typeNamespace.StartsWith(prefix, System.StringComparison.Ordinal))
            && method.Name.EndsWith("Async", System.StringComparison.Ordinal))
            return true;

        // Interface methods on gateway/client types (IPayoutGateway, IStripeClient, etc.)
        if (method.ContainingType?.TypeKind == TypeKind.Interface)
        {
            var name = typeName;
            if ((name.Contains("Gateway") || name.Contains("Client") || name.Contains("Provider"))
                && method.Name.EndsWith("Async", System.StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static SyntaxNode? FindEnclosingUsingScope(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is UsingStatementSyntax usingStatement)
                return usingStatement;

            // using var tx = await ... (local declaration)
            if (ancestor is LocalDeclarationStatementSyntax localDecl && localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
            {
                // Scope is the enclosing block (from using declaration to end of block)
                return localDecl.Parent;
            }
        }
        return null;
    }
}
