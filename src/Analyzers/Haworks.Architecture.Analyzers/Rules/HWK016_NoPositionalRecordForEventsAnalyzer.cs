using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK016_NoPositionalRecordForEventsAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] EventSuffixes = { "Event", "Command", "Message" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoPositionalRecordForEvents);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeRecord, SyntaxKind.RecordDeclaration);
    }

    private static void AnalyzeRecord(SyntaxNodeAnalysisContext context)
    {
        var record = (RecordDeclarationSyntax)context.Node;
        if (record.ParameterList is null || record.ParameterList.Parameters.Count == 0)
            return;

        var name = record.Identifier.Text;
        if (!EventSuffixes.Any(s => name.EndsWith(s, System.StringComparison.Ordinal)))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Diagnostics.NoPositionalRecordForEvents, record.Identifier.GetLocation(), name));
    }
}
