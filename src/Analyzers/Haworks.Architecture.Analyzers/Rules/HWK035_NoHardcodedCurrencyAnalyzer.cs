using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK035_NoHardcodedCurrencyAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] CurrencyCodes = { "USD", "EUR", "GBP", "CAD", "AUD", "JPY", "CHF", "CNY" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoHardcodedCurrency);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeStringLiteral, SyntaxKind.StringLiteralExpression);
    }

    private static void AnalyzeStringLiteral(SyntaxNodeAnalysisContext context)
    {
        var literal = (LiteralExpressionSyntax)context.Node;
        var value = literal.Token.ValueText;

        if (string.IsNullOrEmpty(value) || value.Length != 3)
            return;

        foreach (var code in CurrencyCodes)
        {
            if (string.Equals(value, code, System.StringComparison.Ordinal))
            {
                // Skip test files and constants definitions
                var filePath = context.Node.SyntaxTree.FilePath ?? "";
                if (filePath.Contains("/tests/") || filePath.Contains("/Tests/"))
                    return;

                // Skip if it's in a constant field declaration (defining the config)
                var field = context.Node.FirstAncestorOrSelf<FieldDeclarationSyntax>();
                if (field?.Modifiers.Any(SyntaxKind.ConstKeyword) == true)
                    return;

                // Skip domain entities, DTOs, Options/Config (default currency in property initializers is valid)
                var containingClass = context.Node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
                var className = containingClass?.Identifier.Text ?? "";
                if (className.Contains("Options") || className.Contains("Config") || className.Contains("Settings"))
                    return;

                // Skip Domain entities (currency defaults) and record DTOs
                var nsDecl = context.Node.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>();
                var ns = nsDecl?.Name.ToString() ?? "";
                if (ns.Contains("Domain") || ns.Contains("Contracts"))
                    return;

                // Skip property initializers (e.g., public string Currency { get; init; } = "USD")
                if (context.Node.Parent is EqualsValueClauseSyntax)
                    return;

                // Skip switch expression arms only inside BuildingBlocks.Common (e.g., Money.GetExponent
                // currency lookup table). Exempting all switch expressions would allow service-layer
                // code like `currency switch { "USD" => 0.029m, ... }` to slip through undetected.
                if (context.Node.FirstAncestorOrSelf<SwitchExpressionArmSyntax>() != null)
                {
                    var containingNs = context.SemanticModel
                        .GetEnclosingSymbol(context.Node.SpanStart)
                        ?.ContainingNamespace
                        ?.ToDisplayString() ?? "";
                    if (containingNs.Contains("BuildingBlocks.Common"))
                        return;
                }

                context.ReportDiagnostic(
                    Diagnostic.Create(Diagnostics.NoHardcodedCurrency, literal.GetLocation(), value));
                return;
            }
        }
    }
}
