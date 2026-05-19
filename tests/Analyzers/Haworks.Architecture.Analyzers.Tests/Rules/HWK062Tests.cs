using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK062Tests
{
    [Fact]
    public async Task Guid_Empty_Assignment_Reports()
    {
        const string source = """
            using System;
            public class MyService
            {
                public void DoWork()
                {
                    var id = {|#0:Guid.Empty|};
                }
            }
            """;
        var expected = CSharpAnalyzerVerifier<HWK062_NoGuidEmptyAnalyzer>
            .Diagnostic(Diagnostics.NoGuidEmpty)
            .WithLocation(0)
            .WithArguments("identifier");
        await CSharpAnalyzerVerifier<HWK062_NoGuidEmptyAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Guid_Empty_In_Comparison_NoDiagnostic()
    {
        const string source = """
            using System;
            public class MyService
            {
                public bool IsEmpty(Guid id)
                {
                    return id == Guid.Empty;
                }
            }
            """;
        await CSharpAnalyzerVerifier<HWK062_NoGuidEmptyAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
