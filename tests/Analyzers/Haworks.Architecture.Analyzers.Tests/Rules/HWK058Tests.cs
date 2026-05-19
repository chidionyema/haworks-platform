using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK058Tests
{
    [Fact]
    public async Task Hardcoded_Password_Reports()
    {
        const string source = """
            public class Config
            {
                public void Setup()
                {
                    var password = {|#0:"supersecret123"|};
                }
            }
            """;
        var expected = CSharpAnalyzerVerifier<HWK058_NoSecretsInSourceAnalyzer>
            .Diagnostic(Diagnostics.NoSecretsInSource)
            .WithLocation(0)
            .WithArguments("password");
        await CSharpAnalyzerVerifier<HWK058_NoSecretsInSourceAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Normal_String_NoDiagnostic()
    {
        const string source = """
            public class Config
            {
                public void Setup()
                {
                    var name = "hello world";
                }
            }
            """;
        await CSharpAnalyzerVerifier<HWK058_NoSecretsInSourceAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Short_Value_NoDiagnostic()
    {
        const string source = """
            public class Config
            {
                public void Setup()
                {
                    var password = "short";
                }
            }
            """;
        await CSharpAnalyzerVerifier<HWK058_NoSecretsInSourceAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
