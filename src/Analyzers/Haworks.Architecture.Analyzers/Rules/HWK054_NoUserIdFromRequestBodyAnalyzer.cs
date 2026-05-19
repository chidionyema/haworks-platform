using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK054: State-changing endpoints must not accept userId from the request body.
/// UserId must come from JWT claims to prevent IDOR attacks.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK054_NoUserIdFromRequestBodyAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] StateChangingAttributes =
        { "HttpPost", "HttpPut", "HttpPatch", "HttpDelete" };

    private static readonly string[] UserIdParameterNames =
        { "userId", "UserId", "user_id", "ownerId", "OwnerId", "createdBy", "CreatedBy" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoUserIdFromRequestBody);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        // Only check methods with state-changing HTTP attributes
        bool isStateChanging = method.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(attr => StateChangingAttributes.Any(sa =>
                attr.Name.ToString().Contains(sa)));

        if (!isStateChanging)
            return;

        // Check parameters for userId-like names with [FromBody]
        foreach (var param in method.ParameterList.Parameters)
        {
            var paramName = param.Identifier.Text;
            if (!UserIdParameterNames.Any(n => string.Equals(paramName, n, System.StringComparison.OrdinalIgnoreCase)))
                continue;

            // Check if it has [FromBody] or no attribute (default body binding for complex types)
            bool hasFromBody = param.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString().Contains("FromBody"));

            bool hasFromRoute = param.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString().Contains("FromRoute") ||
                          a.Name.ToString().Contains("FromQuery") ||
                          a.Name.ToString().Contains("FromHeader"));

            if (hasFromBody || !hasFromRoute)
            {
                // Check if it's a simple type (string, Guid) — those aren't body-bound by default
                var typeName = param.Type?.ToString() ?? "";
                if (typeName is "string" or "Guid" or "System.Guid" && !hasFromBody)
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.NoUserIdFromRequestBody,
                    param.GetLocation(),
                    paramName));
            }
        }

        // Check [FromBody] complex type properties for userId
        foreach (var param in method.ParameterList.Parameters)
        {
            bool hasFromBody = param.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString().Contains("FromBody"));

            if (!hasFromBody)
                continue;

            var typeName = param.Type?.ToString() ?? "";
            if (typeName is "string" or "Guid" or "int" or "long")
                continue;

            // Can't resolve the type at syntax level for property inspection,
            // but flag the parameter if it's a command with userId in the name
            if (UserIdParameterNames.Any(n => typeName.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) >= 0))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.NoUserIdFromRequestBody,
                    param.GetLocation(),
                    typeName));
            }
        }
    }
}
