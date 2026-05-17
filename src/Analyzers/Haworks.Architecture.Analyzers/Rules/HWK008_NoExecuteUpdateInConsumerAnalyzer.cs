using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK008_NoExecuteUpdateInConsumerAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] BannedMethods = { "ExecuteUpdateAsync", "ExecuteDeleteAsync", "ExecuteUpdate", "ExecuteDelete" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoExecuteUpdateInConsumer);

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

        var methodName = ma.Name.Identifier.Text;
        if (!IsBanned(methodName))
            return;

        var classDecl = invocation.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (classDecl is null)
            return;

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDecl, context.CancellationToken);
        if (classSymbol is null || !ImplementsIConsumer(classSymbol))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Diagnostics.NoExecuteUpdateInConsumer, invocation.GetLocation(), methodName));
    }

    private static bool IsBanned(string name)
    {
        foreach (var b in BannedMethods)
            if (string.Equals(name, b, System.StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool ImplementsIConsumer(INamedTypeSymbol classSymbol)
    {
        foreach (var iface in classSymbol.AllInterfaces)
        {
            if (iface.IsGenericType &&
                iface.OriginalDefinition.ToDisplayString() == "MassTransit.IConsumer<T>")
                return true;
        }
        return false;
    }
}
