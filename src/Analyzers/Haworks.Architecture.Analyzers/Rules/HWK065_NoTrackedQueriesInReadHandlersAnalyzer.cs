using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK065: Query handlers (Get*, List*, Search*) should use AsNoTracking
/// since they don't modify entities.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK065_NoTrackedQueriesInReadHandlersAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] QueryPrefixes =
        { "Get", "List", "Search", "Find", "Fetch", "Query", "Browse" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoTrackedQueriesInReadHandlers);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeClass(SyntaxNodeAnalysisContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var className = classDecl.Identifier.Text;

        // Must be a query handler
        if (!className.EndsWith("Handler", System.StringComparison.Ordinal) &&
            !className.EndsWith("QueryHandler", System.StringComparison.Ordinal))
            return;

        bool isQueryHandler = QueryPrefixes.Any(p =>
            className.StartsWith(p, System.StringComparison.Ordinal));
        if (!isQueryHandler)
            return;

        // Skip test files
        var filePath = context.Node.SyntaxTree.FilePath ?? "";
        if (filePath.Contains("/tests/") || filePath.Contains("/Tests/") ||
            filePath.Contains("\\tests\\") || filePath.Contains("\\Tests\\"))
            return;

        var classText = classDecl.ToString();

        // Check if class accesses DbContext/DbSet (performs queries)
        bool hasDbAccess = classText.Contains("DbContext") || classText.Contains("DbSet") ||
                           classText.Contains("ToListAsync") || classText.Contains("FirstOrDefaultAsync") ||
                           classText.Contains("SingleOrDefaultAsync");
        if (!hasDbAccess)
            return;

        // Check if AsNoTracking is used
        if (classText.Contains("AsNoTracking") || classText.Contains("NoTracking"))
            return;

        // Check if it has SaveChanges (then it's not read-only)
        if (classText.Contains("SaveChanges"))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.NoTrackedQueriesInReadHandlers,
            classDecl.Identifier.GetLocation(),
            className));
    }
}
