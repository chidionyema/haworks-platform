using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK053: Methods in ledger/financial services that call SaveChangesAsync
/// must be wrapped in a transaction to prevent partial commits.
/// Exempts MassTransit consumers (they get transactions from the outbox).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK053_FinancialWritesMustUseTransactionAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] FinancialClassPatterns =
        { "Ledger", "Payment", "Refund", "Payout", "Transfer", "Balance", "Invoice" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.FinancialWritesMustUseTransaction);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (method.Body == null && method.ExpressionBody == null)
            return;

        // Skip test files
        var filePath = context.Node.SyntaxTree.FilePath ?? "";
        if (filePath.Contains("/tests/") || filePath.Contains("/Tests/") ||
            filePath.Contains("\\tests\\") || filePath.Contains("\\Tests\\"))
            return;

        // Only apply to financial service classes
        var classDecl = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDecl == null)
            return;

        var className = classDecl.Identifier.Text;
        bool isFinancialClass = FinancialClassPatterns.Any(p =>
            className.IndexOf(p, System.StringComparison.Ordinal) >= 0);
        if (!isFinancialClass)
            return;

        // Exempt consumers (outbox provides transaction)
        var baseList = classDecl.BaseList?.ToString() ?? "";
        if (baseList.Contains("IConsumer") || baseList.Contains("ConsumerBase"))
            return;

        // Check if method has SaveChangesAsync
        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        bool hasSaveChanges = invocations.Any(inv =>
        {
            var name = GetMethodName(inv);
            return name is "SaveChangesAsync" or "SaveChanges";
        });

        if (!hasSaveChanges)
            return;

        // Check if method has a transaction
        bool hasTransaction = invocations.Any(inv =>
        {
            var name = GetMethodName(inv);
            return name is "BeginTransactionAsync" or "BeginTransaction";
        });

        // Also check for using statement with transaction
        bool hasUsingTransaction = method.DescendantNodes().OfType<UsingStatementSyntax>()
            .Any(u => u.ToString().Contains("Transaction"));
        bool hasUsingDeclTransaction = method.DescendantNodes().OfType<LocalDeclarationStatementSyntax>()
            .Any(ld => ld.UsingKeyword.IsKind(SyntaxKind.UsingKeyword) &&
                       ld.ToString().Contains("Transaction"));

        if (!hasTransaction && !hasUsingTransaction && !hasUsingDeclTransaction)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.FinancialWritesMustUseTransaction,
                method.Identifier.GetLocation(),
                method.Identifier.Text));
        }
    }

    private static string GetMethodName(InvocationExpressionSyntax inv)
    {
        return inv.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => ""
        };
    }
}
