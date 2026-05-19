using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK075: Detects the "sandwich" anti-pattern where a method does:
///   SaveChangesAsync → external HTTP/API call → SaveChangesAsync
/// without three-phase transaction separation.
///
/// Correct pattern: Phase 1 (lock+pending+commit) → Phase 2 (external I/O) → Phase 3 (re-lock+settle+commit)
/// Each phase must use its own transaction. A single method with SaveChanges-ExternalIO-SaveChanges
/// in sequence (without separate BeginTransaction per phase) is a violation.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK075_NoDbWriteExternalIoDbWriteSandwichAnalyzer : DiagnosticAnalyzer
{
    private static readonly HashSet<string> DbWriteMethods = new()
    {
        "SaveChangesAsync", "SaveChanges"
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

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.DbWriteExternalIoDbWriteSandwich);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (method.Body == null) return;

        var semanticModel = context.SemanticModel;

        // Collect all invocations in source order with their classification
        var calls = new List<(InvocationExpressionSyntax Node, CallKind Kind, string Name, int Line)>();

        foreach (var invocation in method.Body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (semanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is not IMethodSymbol calledMethod) continue;

            var kind = Classify(calledMethod);
            if (kind != CallKind.Other)
            {
                var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                calls.Add((invocation, kind, calledMethod.Name, line));
            }
        }

        // Look for the sandwich: DbWrite → ExternalIo → DbWrite
        for (int i = 0; i < calls.Count; i++)
        {
            if (calls[i].Kind != CallKind.DbWrite) continue;

            for (int j = i + 1; j < calls.Count; j++)
            {
                if (calls[j].Kind != CallKind.ExternalIo) continue;

                for (int k = j + 1; k < calls.Count; k++)
                {
                    if (calls[k].Kind != CallKind.DbWrite) continue;

                    // Found sandwich: calls[i] → calls[j] → calls[k]
                    // Check if they share a single transaction scope (no separate BeginTransaction between phases)
                    if (!HasSeparateTransactionPerPhase(calls[i].Node, calls[j].Node, calls[k].Node, method))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.DbWriteExternalIoDbWriteSandwich,
                            calls[j].Node.GetLocation(),
                            calls[i].Line,
                            calls[j].Name,
                            calls[j].Line,
                            calls[k].Line));
                        return; // One diagnostic per method is enough
                    }
                }
            }
        }
    }

    private static CallKind Classify(IMethodSymbol method)
    {
        if (DbWriteMethods.Contains(method.Name))
            return CallKind.DbWrite;

        var typeName = method.ContainingType?.Name ?? "";
        var typeNamespace = method.ContainingType?.ContainingNamespace?.ToDisplayString() ?? "";

        if (ExternalIoContainingTypes.Contains(typeName) && ExternalIoMethods.Contains(method.Name))
            return CallKind.ExternalIo;

        // Known external SDK namespaces
        if ((typeNamespace.StartsWith("System.Net.Http") ||
             typeNamespace.StartsWith("Stripe") ||
             typeNamespace.StartsWith("PayPal") ||
             typeNamespace.StartsWith("Amazon.S3"))
            && method.Name.EndsWith("Async"))
            return CallKind.ExternalIo;

        // Interface gateway/client types
        if (method.ContainingType?.TypeKind == TypeKind.Interface)
        {
            if ((typeName.Contains("Gateway") || typeName.Contains("Client") || typeName.Contains("Provider"))
                && method.Name.EndsWith("Async"))
                return CallKind.ExternalIo;
        }

        return CallKind.Other;
    }

    /// <summary>
    /// Returns true if there are separate BeginTransaction calls isolating each phase.
    /// The correct pattern has: BeginTx1 → SaveChanges1 → CommitTx1 → externalCall → BeginTx2 → SaveChanges2 → CommitTx2.
    /// We check: the first SaveChanges and the external call must NOT share the same using-transaction scope,
    /// AND the external call and second SaveChanges must NOT share the same using-transaction scope.
    /// </summary>
    private static bool HasSeparateTransactionPerPhase(
        InvocationExpressionSyntax firstWrite,
        InvocationExpressionSyntax externalCall,
        InvocationExpressionSyntax secondWrite,
        MethodDeclarationSyntax method)
    {
        var firstWriteScope = FindEnclosingTransactionScope(firstWrite);
        var externalCallScope = FindEnclosingTransactionScope(externalCall);
        var secondWriteScope = FindEnclosingTransactionScope(secondWrite);

        // If external call is NOT in any transaction scope, and the two writes are in
        // different transaction scopes, this is the correct three-phase pattern.
        if (externalCallScope == null && firstWriteScope != null && secondWriteScope != null
            && firstWriteScope != secondWriteScope)
            return true;

        return false;
    }

    private static SyntaxNode? FindEnclosingTransactionScope(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is UsingStatementSyntax)
                return ancestor;

            if (ancestor is BlockSyntax block)
            {
                // Check if any local declaration in this block is a using-var for a transaction
                foreach (var statement in block.Statements)
                {
                    if (statement is LocalDeclarationStatementSyntax localDecl
                        && localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)
                        && localDecl.Span.Start <= node.Span.Start)
                    {
                        var declText = localDecl.ToString();
                        if (declText.Contains("BeginTransaction"))
                            return block;
                    }
                }
            }
        }
        return null;
    }

    private enum CallKind { Other, DbWrite, ExternalIo }
}
