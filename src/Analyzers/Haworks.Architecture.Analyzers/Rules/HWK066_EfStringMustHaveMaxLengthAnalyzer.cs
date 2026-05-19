using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK066: EF string property configurations must have MaxLength or HasColumnType("text").
/// Without it, Postgres uses unbounded text which prevents indexing and wastes storage.
/// Checks OnModelCreating/IEntityTypeConfiguration for Property(x => x.StringProp) chains.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK066_EfStringMustHaveMaxLengthAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.EfStringMustHaveMaxLength);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax ma)
            return;

        // Look for .Property(x => x.SomeProp) calls
        if (ma.Name.Identifier.Text != "Property")
            return;

        // Must be in an EF configuration method
        var method = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method == null)
            return;

        var methodName = method.Identifier.Text;
        if (methodName is not ("Configure" or "OnModelCreating"))
            return;

        // Get the property name from the lambda
        if (invocation.ArgumentList.Arguments.Count == 0)
            return;

        var arg = invocation.ArgumentList.Arguments[0].Expression;
        if (arg is not SimpleLambdaExpressionSyntax lambda)
            return;

        var propAccess = lambda.Body as MemberAccessExpressionSyntax;
        var propName = propAccess?.Name.Identifier.Text ?? "";

        // Get the full fluent chain from this Property call
        var statement = invocation.FirstAncestorOrSelf<StatementSyntax>();
        if (statement == null)
            return;

        var chainText = statement.ToString();

        // Check if chain has MaxLength, HasMaxLength, or HasColumnType
        if (chainText.Contains("HasMaxLength") || chainText.Contains("MaxLength") ||
            chainText.Contains("HasColumnType"))
            return;

        // Only flag if this looks like it could be a string property
        // (we can't resolve types at syntax level, but skip obvious non-strings)
        if (chainText.Contains("HasPrecision") || chainText.Contains("HasConversion") ||
            chainText.Contains("ValueGeneratedOn"))
            return;

        // Heuristic: check if the property name suggests it's a string
        var lowerProp = propName.ToLowerInvariant();
        if (lowerProp.Contains("name") || lowerProp.Contains("description") ||
            lowerProp.Contains("email") || lowerProp.Contains("title") ||
            lowerProp.Contains("address") || lowerProp.Contains("phone") ||
            lowerProp.Contains("code") || lowerProp.Contains("status") ||
            lowerProp.Contains("type") || lowerProp.Contains("url") ||
            lowerProp.Contains("path") || lowerProp.Contains("currency") ||
            lowerProp.EndsWith("id", System.StringComparison.OrdinalIgnoreCase))
        {
            // Check if IsRequired is in chain (strong hint it's a string)
            if (chainText.Contains("IsRequired"))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.EfStringMustHaveMaxLength,
                    invocation.GetLocation(),
                    propName));
            }
        }
    }
}
