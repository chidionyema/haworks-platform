using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK007: Inside an IConsumer, all event publishing must go through ConsumeContext.Publish.
/// Using IPublishEndpoint or IDomainEventPublisher bypasses the consumer's ambient
/// outbox transaction, creating a dual-write risk.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK007_MustUseContextPublishInConsumerAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.MustUseContextPublishInConsumer);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;
        if (methodName is not ("Publish" or "PublishAsync" or "Send" or "SendAsync"))
            return;

        // Check we're inside a consumer class
        var classDecl = invocation.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (classDecl is null)
            return;

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDecl, context.CancellationToken);
        if (classSymbol is null || !ImplementsIConsumer(classSymbol))
            return;

        // Now check what the receiver type is
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        var receiverType = methodSymbol.ContainingType?.ToDisplayString() ?? "";

        // ALLOWED: ConsumeContext.Publish (this is the correct pattern)
        if (receiverType.Contains("ConsumeContext"))
            return;

        // FLAG: IPublishEndpoint, IBus, IDomainEventPublisher, ISendEndpointProvider
        if (IsProhibitedPublisher(receiverType))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Diagnostics.MustUseContextPublishInConsumer,
                    invocation.GetLocation(), $"{receiverType}.{methodName}"));
        }
    }

    private static bool IsProhibitedPublisher(string typeName) =>
        typeName.Contains("IPublishEndpoint") ||
        typeName.Contains("IBus") ||
        typeName.Contains("IDomainEventPublisher") ||
        typeName.Contains("ISendEndpointProvider") ||
        typeName.Contains("IEventPublisher");

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
