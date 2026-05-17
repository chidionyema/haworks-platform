using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK019Tests
{
    [Fact]
    public async Task Localhost_InString_Reports()
    {
        const string source = """
            public class Cfg { public string Url = {|#0:"http://localhost:5000"|}; }
            """;
        var expected = CSharpAnalyzerVerifier<HWK019_NoHardcodedLocalhostAnalyzer>
            .Diagnostic(Diagnostics.NoHardcodedLocalhost).WithLocation(0).WithArguments("localhost");
        await CSharpAnalyzerVerifier<HWK019_NoHardcodedLocalhostAnalyzer>.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task RealUrl_NoDiagnostic()
    {
        const string source = """
            public class Cfg { public string Url = "https://api.haworks.com"; }
            """;
        await CSharpAnalyzerVerifier<HWK019_NoHardcodedLocalhostAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }
}
