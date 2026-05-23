using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK086: Records/classes with a monetary amount property (Amount, AmountCents, TotalAmount,
/// UnitPrice, etc.) must also declare a Currency property in the same type.
/// Prevents silent currency-blindness where amounts flow without knowing their denomination.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK086_MonetaryAmountMustHaveCurrencyAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: "HWK086",
        title: "Monetary amount property must have a paired Currency property",
        messageFormat: "Property '{0}' looks like a monetary amount but type '{1}' has no Currency property",
        category: "Finance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Every monetary amount must be paired with a currency code to prevent silent denomination errors.");

    private static readonly string[] MonetaryPatterns =
    [
        "Amount", "AmountCents", "TotalAmount", "TotalAmountCents",
        "UnitPrice", "UnitPriceCents", "SubTotal", "SubTotalCents",
        "Tax", "TaxCents", "TaxAmount", "Commission", "Balance",
        "DiscountAmount", "RefundAmount", "RateAmount"
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeType, SyntaxKind.RecordDeclaration, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeType(SyntaxNodeAnalysisContext context)
    {
        var typeDecl = (TypeDeclarationSyntax)context.Node;
        var typeName = typeDecl.Identifier.Text;

        // Only check types in Contracts namespace (events, DTOs)
        var ns = GetNamespace(typeDecl);
        if (ns == null || !ns.Contains("Contracts")) return;

        // Skip types that already have a Currency property
        var members = typeDecl.Members;
        var hasCurrency = members.OfType<PropertyDeclarationSyntax>()
            .Any(p => p.Identifier.Text.Equals("Currency", System.StringComparison.OrdinalIgnoreCase) ||
                      p.Identifier.Text.Equals("CurrencyCode", System.StringComparison.OrdinalIgnoreCase));

        // Also check positional record parameters
        if (!hasCurrency && typeDecl is RecordDeclarationSyntax record && record.ParameterList != null)
        {
            hasCurrency = record.ParameterList.Parameters
                .Any(p => p.Identifier.Text.Equals("Currency", System.StringComparison.OrdinalIgnoreCase) ||
                          p.Identifier.Text.Equals("CurrencyCode", System.StringComparison.OrdinalIgnoreCase));
        }

        if (hasCurrency) return;

        // Check if any property matches a monetary pattern
        foreach (var prop in members.OfType<PropertyDeclarationSyntax>())
        {
            var propName = prop.Identifier.Text;
            if (MonetaryPatterns.Any(p => propName.Equals(p, System.StringComparison.OrdinalIgnoreCase)))
            {
                // Check the type is numeric (decimal, long, int)
                var typeStr = prop.Type.ToString();
                if (typeStr is "decimal" or "long" or "int" or "double" or "float")
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, prop.GetLocation(), propName, typeName));
                    return; // One diagnostic per type is enough
                }
            }
        }
    }

    private static string? GetNamespace(SyntaxNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current is BaseNamespaceDeclarationSyntax ns)
                return ns.Name.ToString();
            if (current is FileScopedNamespaceDeclarationSyntax fsns)
                return fsns.Name.ToString();
            current = current.Parent;
        }
        return null;
    }
}
