using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK072Tests
{
    [Fact]
    public async Task CatchBlock_SwallowsException_InsidePollyExecute_Reports()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Polly;
            public class Service
            {
                public async Task<string> CallApi(IAsyncPolicy policy)
                {
                    return await policy.ExecuteAsync<string>(async () =>
                    {
                        try
                        {
                            return await Task.FromResult("ok");
                        }
                        {|#0:catch|} (Exception)
                        {
                            return "fallback";
                        }
                    });
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<HWK072_NoSwallowedExceptionInPollyAnalyzer>
            .Diagnostic(Diagnostics.SwallowedPollyException)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<HWK072_NoSwallowedExceptionInPollyAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task CatchBlock_Rethrows_InsidePollyExecute_NoDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Polly;
            public class Service
            {
                public async Task<string> CallApi(IAsyncPolicy policy)
                {
                    return await policy.ExecuteAsync<string>(async () =>
                    {
                        try
                        {
                            return await Task.FromResult("ok");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex);
                            throw;
                        }
                    });
                }
            }
            """;

        await CSharpAnalyzerVerifier<HWK072_NoSwallowedExceptionInPollyAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CatchBlock_OutsidePollyExecute_NoDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            public class Service
            {
                public async Task<string> CallApi()
                {
                    try
                    {
                        return await Task.FromResult("ok");
                    }
                    catch (Exception)
                    {
                        return "fallback";
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<HWK072_NoSwallowedExceptionInPollyAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
