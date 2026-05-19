using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK059Tests
{
    [Fact]
    public async Task Throw_Ex_Reports()
    {
        const string source = """
            using System;
            public class MyService
            {
                public void DoWork()
                {
                    try { }
                    catch (Exception ex)
                    {
                        {|#0:throw ex;|}
                    }
                }
            }
            """;
        var expected = CSharpAnalyzerVerifier<HWK059_NoThrowExAnalyzer>
            .Diagnostic(Diagnostics.NoThrowEx)
            .WithLocation(0)
            .WithArguments("ex");
        await CSharpAnalyzerVerifier<HWK059_NoThrowExAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Throw_Without_Ex_NoDiagnostic()
    {
        const string source = """
            using System;
            public class MyService
            {
                public void DoWork()
                {
                    try { }
                    catch (Exception ex)
                    {
                        throw;
                    }
                }
            }
            """;
        await CSharpAnalyzerVerifier<HWK059_NoThrowExAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Throw_New_Exception_NoDiagnostic()
    {
        const string source = """
            using System;
            public class MyService
            {
                public void DoWork()
                {
                    try { }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("msg", ex);
                    }
                }
            }
            """;
        await CSharpAnalyzerVerifier<HWK059_NoThrowExAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
