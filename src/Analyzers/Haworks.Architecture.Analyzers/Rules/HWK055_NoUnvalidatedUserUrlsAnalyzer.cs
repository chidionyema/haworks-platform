using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK055: User-supplied URLs passed to HttpClient without SSRF validation.
/// Detects patterns like httpClient.GetAsync(userUrl) where userUrl comes from
/// a parameter or request body.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK055_NoUnvalidatedUserUrlsAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] HttpMethods =
        { "GetAsync", "PostAsync", "PutAsync", "DeleteAsync", "PatchAsync",
          "SendAsync", "GetStringAsync", "GetStreamAsync", "GetByteArrayAsync" };

    private static readonly string[] UrlParamNames =
        { "url", "uri", "endpoint", "callbackUrl", "webhookUrl", "notificationUrl",
          "returnUrl", "redirectUrl", "imageUrl", "avatarUrl", "targetUrl" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoUnvalidatedUserUrls);

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
        if (!HttpMethods.Any(m => m == methodName))
            return;

        // Check if receiver looks like an HttpClient
        var receiverText = ma.Expression.ToString();
        if (receiverText.IndexOf("client", System.StringComparison.OrdinalIgnoreCase) < 0 &&
            receiverText.IndexOf("http", System.StringComparison.OrdinalIgnoreCase) < 0)
            return;

        if (invocation.ArgumentList.Arguments.Count == 0)
            return;

        var firstArg = invocation.ArgumentList.Arguments[0].Expression;
        var argText = firstArg.ToString().ToLowerInvariant();

        // Check if the URL argument is a user-controllable variable
        if (firstArg is IdentifierNameSyntax id)
        {
            var varName = id.Identifier.Text;
            if (UrlParamNames.Any(p =>
                varName.IndexOf(p, System.StringComparison.OrdinalIgnoreCase) >= 0))
            {
                // Check if there's an SSRF guard call before this invocation
                var method = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                if (method != null && !HasSsrfValidation(method, varName))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.NoUnvalidatedUserUrls,
                        firstArg.GetLocation(),
                        varName));
                }
            }
        }
    }

    private static bool HasSsrfValidation(MethodDeclarationSyntax method, string varName)
    {
        var methodText = method.ToString();
        return methodText.Contains("SsrfGuard") ||
               methodText.Contains("ValidateUrl") ||
               methodText.Contains("IsAllowedUrl") ||
               methodText.Contains("AllowedHosts") ||
               methodText.Contains("UrlValidator");
    }
}
