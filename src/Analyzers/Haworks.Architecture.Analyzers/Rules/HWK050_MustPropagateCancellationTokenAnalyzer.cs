using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK050: Detects awaited method calls where:
/// 1. The called method has an overload that accepts CancellationToken
/// 2. A CancellationToken is available in the current scope (parameter, local, or member access)
/// 3. No CancellationToken argument was actually passed
///
/// This catches the #1 agent mistake: calling async methods without forwarding the token.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK050_MustPropagateCancellationTokenAnalyzer : DiagnosticAnalyzer
{
    private const string CancellationTokenType = "System.Threading.CancellationToken";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.MustPropagateCancellationToken);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAwait, SyntaxKind.AwaitExpression);
    }

    private static void AnalyzeAwait(SyntaxNodeAnalysisContext context)
    {
        var awaitExpr = (AwaitExpressionSyntax)context.Node;

        // Get the invocation being awaited
        var invocation = awaitExpr.Expression as InvocationExpressionSyntax;
        if (invocation is null)
        {
            // Handle: await obj.Method(...) where the await is on the result
            if (awaitExpr.Expression is InvocationExpressionSyntax directInvocation)
                invocation = directInvocation;
            else
                return;
        }

        // Check if a CancellationToken argument is already being passed
        if (AlreadyPassesCancellationToken(invocation, context.SemanticModel, context.CancellationToken))
            return;

        // Check if the method has an overload accepting CancellationToken
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol;
        if (methodSymbol is null)
            return;

        // Skip if the method itself has no CT-accepting overload
        if (!HasCancellationTokenOverload(methodSymbol))
            return;

        // Check if a CancellationToken is available in the enclosing scope
        var tokenName = FindAvailableCancellationToken(invocation, context.SemanticModel, context.CancellationToken);
        if (tokenName is null)
            return;

        var methodName = methodSymbol.Name;
        context.ReportDiagnostic(
            Diagnostic.Create(Diagnostics.MustPropagateCancellationToken,
                invocation.GetLocation(), methodName, tokenName));
    }

    private static bool AlreadyPassesCancellationToken(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        System.Threading.CancellationToken ct)
    {
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            var typeInfo = model.GetTypeInfo(arg.Expression, ct);
            if (typeInfo.Type?.ToDisplayString() == CancellationTokenType)
                return true;
        }
        return false;
    }

    private static bool HasCancellationTokenOverload(IMethodSymbol method)
    {
        // Check if the method itself already accepts CT (but caller didn't pass it)
        if (method.Parameters.Any(p => p.Type.ToDisplayString() == CancellationTokenType))
            return true;

        // Check sibling overloads in the same type
        var containingType = method.ContainingType;
        if (containingType is null)
            return false;

        var overloads = containingType.GetMembers(method.Name).OfType<IMethodSymbol>();
        return overloads.Any(overload =>
            overload.Parameters.Any(p => p.Type.ToDisplayString() == CancellationTokenType) &&
            !SymbolEqualityComparer.Default.Equals(overload, method));
    }

    private static string? FindAvailableCancellationToken(
        SyntaxNode node,
        SemanticModel model,
        System.Threading.CancellationToken ct)
    {
        // Walk up to find the enclosing method
        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method is null) return null;

        // Check parameters
        foreach (var param in method.ParameterList.Parameters)
        {
            var paramSymbol = model.GetDeclaredSymbol(param, ct);
            if (paramSymbol?.Type.ToDisplayString() == CancellationTokenType)
                return param.Identifier.Text;
        }

        // Check for common patterns: context.CancellationToken, _ct, cancellationToken
        // Look for local variables or member accesses that resolve to CancellationToken
        var locals = method.DescendantNodes().OfType<LocalDeclarationStatementSyntax>();
        foreach (var local in locals)
        {
            foreach (var variable in local.Declaration.Variables)
            {
                var localSymbol = model.GetDeclaredSymbol(variable, ct);
                if (localSymbol is ILocalSymbol ls && ls.Type.ToDisplayString() == CancellationTokenType)
                    return variable.Identifier.Text;
            }
        }

        return null;
    }
}
