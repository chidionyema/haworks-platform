using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK061Tests
{
    [Fact]
    public async Task Todo_Comment_Reports()
    {
        const string source = """
            public class MyService
            {
                {|#0:// TODO: fix this later|}
                public void DoWork() { }
            }
            """;
        var expected = CSharpAnalyzerVerifier<HWK061_NoTodoCommentsAnalyzer>
            .Diagnostic(Diagnostics.NoTodoComments)
            .WithLocation(0);
        await CSharpAnalyzerVerifier<HWK061_NoTodoCommentsAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Normal_Comment_NoDiagnostic()
    {
        const string source = """
            public class MyService
            {
                // This method processes orders
                public void DoWork() { }
            }
            """;
        await CSharpAnalyzerVerifier<HWK061_NoTodoCommentsAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
