using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK067: Record types should not be used as EF Core entities.
/// Records have value equality semantics that break EF change tracking.
/// Detects records that inherit from entity base classes or are configured
/// in DbContext.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK067_NoEntityRecordsWithEfAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] EntityBaseTypes =
        { "Entity", "BaseEntity", "AggregateRoot", "AuditableEntity", "DomainEntity" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoEntityRecordsWithEf);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeRecord, SyntaxKind.RecordDeclaration);
    }

    private static void AnalyzeRecord(SyntaxNodeAnalysisContext context)
    {
        var record = (RecordDeclarationSyntax)context.Node;

        // Check if record inherits from an entity base type
        if (record.BaseList == null)
            return;

        var baseList = record.BaseList.ToString();
        bool isEntity = EntityBaseTypes.Any(bt =>
            baseList.IndexOf(bt, System.StringComparison.Ordinal) >= 0);

        if (!isEntity)
            return;

        // Skip event/command records — those are fine as records
        var name = record.Identifier.Text;
        if (name.EndsWith("Event", System.StringComparison.Ordinal) ||
            name.EndsWith("Command", System.StringComparison.Ordinal) ||
            name.EndsWith("Query", System.StringComparison.Ordinal) ||
            name.EndsWith("Dto", System.StringComparison.Ordinal) ||
            name.EndsWith("Response", System.StringComparison.Ordinal) ||
            name.EndsWith("Request", System.StringComparison.Ordinal))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.NoEntityRecordsWithEf,
            record.Identifier.GetLocation(),
            name));
    }
}
