using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haworks.Architecture.Analyzers.Rules;

/// <summary>
/// HWK058: Detects hardcoded secrets (API keys, passwords, connection strings)
/// in source code via assignment patterns.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HWK058_NoSecretsInSourceAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] SecretVariablePatterns =
        { "password", "secret", "apikey", "api_key", "connectionstring",
          "privatekey", "private_key", "accesstoken", "access_token",
          "clientsecret", "client_secret", "authtoken", "auth_token" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.NoSecretsInSource);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
        context.RegisterSyntaxNodeAction(AnalyzeVariableDeclaration, SyntaxKind.VariableDeclarator);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (assignment.Right is not LiteralExpressionSyntax literal ||
            !literal.IsKind(SyntaxKind.StringLiteralExpression))
            return;

        var varName = assignment.Left.ToString();
        CheckForSecret(context, varName, literal);
    }

    private static void AnalyzeVariableDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declarator = (VariableDeclaratorSyntax)context.Node;
        if (declarator.Initializer?.Value is not LiteralExpressionSyntax literal ||
            !literal.IsKind(SyntaxKind.StringLiteralExpression))
            return;

        var varName = declarator.Identifier.Text;
        CheckForSecret(context, varName, literal);
    }

    private static void CheckForSecret(SyntaxNodeAnalysisContext context, string varName, LiteralExpressionSyntax literal)
    {
        var value = literal.Token.ValueText;
        if (string.IsNullOrWhiteSpace(value) || value.Length < 8)
            return;

        // Skip test files and test infrastructure
        var filePath = context.Node.SyntaxTree.FilePath ?? "";
        if (filePath.Contains("/tests/") || filePath.Contains("/Tests/") ||
            filePath.Contains("\\tests\\") || filePath.Contains("\\Tests\\") ||
            filePath.Contains("BuildingBlocks.Testing"))
            return;

        // Skip enum members — names like PasswordChange/PasswordReset are not secrets
        if (context.Node.FirstAncestorOrSelf<EnumMemberDeclarationSyntax>() != null)
            return;

        // Skip config key indexer access like ["Vault:SecretIdPath"]
        if (value.Contains(":") || value.Contains("__"))
            return;

        // Skip const/enum-like values where the value matches the variable name
        // (e.g. const string PasswordChange = "PasswordChange")
        var bareVarName = varName.Contains(".") ? varName.Substring(varName.LastIndexOf('.') + 1) : varName;
        bareVarName = bareVarName.TrimStart('[', '"').TrimEnd(']', '"');
        if (string.Equals(bareVarName, value, System.StringComparison.OrdinalIgnoreCase))
            return;

        // Skip values that look like file paths or URIs (not actual secrets)
        if (value.StartsWith("/", System.StringComparison.Ordinal) ||
            value.StartsWith("\\", System.StringComparison.Ordinal) ||
            value.StartsWith("http://", System.StringComparison.Ordinal) ||
            value.StartsWith("https://", System.StringComparison.Ordinal))
            return;

        // Skip values that look like placeholder/test tokens (sk_test_, pk_test_, etc.)
        var lowerValue = value.ToLowerInvariant();
        if (lowerValue.StartsWith("sk_test", System.StringComparison.Ordinal) ||
            lowerValue.StartsWith("pk_test", System.StringComparison.Ordinal) ||
            lowerValue == "changeme" || lowerValue == "placeholder")
            return;

        var lowerName = varName.Replace(".", "").Replace("_", "").ToLowerInvariant();
        foreach (var pattern in SecretVariablePatterns)
        {
            if (lowerName.Contains(pattern))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.NoSecretsInSource,
                    literal.GetLocation(),
                    varName));
                return;
            }
        }
    }
}
