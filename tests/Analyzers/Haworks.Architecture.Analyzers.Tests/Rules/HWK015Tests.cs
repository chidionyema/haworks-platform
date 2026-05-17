using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK015Tests
{
    [Fact]
    public async Task AsyncVoid_Reports()
    {
        const string source = """
            using System.Threading.Tasks;
            public class Svc
            {
                public async void {|#0:Bad|}() { await Task.Delay(1); }
            }
            """;
        var expected = CSharpAnalyzerVerifier<HWK015_NoAsyncVoidAnalyzer>
            .Diagnostic(Diagnostics.NoAsyncVoid).WithLocation(0).WithArguments("Bad");
        await CSharpAnalyzerVerifier<HWK015_NoAsyncVoidAnalyzer>.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AsyncTask_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            public class Svc { public async Task Good() { await Task.Delay(1); } }
            """;
        await CSharpAnalyzerVerifier<HWK015_NoAsyncVoidAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }
}
