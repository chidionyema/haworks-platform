using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK052: Consumers of financial events must check for duplicate processing.
/// Look for idempotency patterns: IdempotencyJournal, FOR UPDATE, ProcessedEvents, etc.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK052_ConsumerMustCheckIdempotencyAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] FinancialEventSuffixes =
        { "PaymentCompleted", "PaymentFailed", "RefundRequested", "RefundCompleted",
          "PayoutRequested", "PayoutCompleted", "TransferCompleted", "BalanceUpdated",
          "LedgerEntryCreated", "InvoiceCreated", "ChargeCreated" };

    private static readonly string[] IdempotencyPatterns =
        { "IdempotencyJournal", "ProcessedEvent", "FOR UPDATE", "AlreadyProcessed",
          "WasProcessed", "IsProcessed", "IdempotencyKey", "DuplicateCheck" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.ConsumerMustCheckIdempotency);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeClass(SyntaxNodeAnalysisContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var baseList = classDecl.BaseList?.ToString() ?? "";

        if (!baseList.Contains("IConsumer"))
            return;

        // Check if consuming a financial event
        bool isFinancial = FinancialEventSuffixes.Any(s =>
            baseList.IndexOf(s, System.StringComparison.Ordinal) >= 0);
        if (!isFinancial)
            return;

        // Check if the class body contains idempotency patterns
        var classText = classDecl.ToString();
        bool hasIdempotencyCheck = IdempotencyPatterns.Any(p =>
            classText.IndexOf(p, System.StringComparison.OrdinalIgnoreCase) >= 0);

        if (!hasIdempotencyCheck)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.ConsumerMustCheckIdempotency,
                classDecl.Identifier.GetLocation(),
                classDecl.Identifier.Text));
        }
    }
}
