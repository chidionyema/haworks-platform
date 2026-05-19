using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK082Tests
{
    [Fact]
    public async Task HttpClient_GetAsync_Without_Polly_Reports()
    {
        const string source = """
            using System.Threading.Tasks;
            using System.Net.Http;
            public class Service
            {
                private readonly HttpClient _http;
                public async Task<string> CallApi()
                {
                    var result = await {|#0:_http.GetAsync("https://api.example.com")|};
                    return result.ToString();
                }
            }
            """;

        var expected = Verifier<HWK082_NoHttpCallWithoutResiliencePolicyAnalyzer>
            .Diagnostic(Diagnostics.NoHttpCallWithoutResiliencePolicy)
            .WithLocation(0)
            .WithArguments("GetAsync");

        await Verifier<HWK082_NoHttpCallWithoutResiliencePolicyAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task HttpClient_Inside_Polly_ExecuteAsync_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using System.Net.Http;
            using Polly;
            public class Service
            {
                private readonly HttpClient _http;
                private readonly IAsyncPolicy _policy;
                public async Task<HttpResponseMessage> CallApi()
                {
                    return await _policy.ExecuteAsync<HttpResponseMessage>(async () =>
                    {
                        return await _http.GetAsync("https://api.example.com");
                    });
                }
            }
            """;

        await Verifier<HWK082_NoHttpCallWithoutResiliencePolicyAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HttpClient_In_Test_Class_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using System.Net.Http;
            public class ApiTests
            {
                private readonly HttpClient _http;
                public async Task TestEndpoint()
                {
                    var result = await _http.GetAsync("https://localhost");
                }
            }
            """;

        await Verifier<HWK082_NoHttpCallWithoutResiliencePolicyAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    private static class Verifier<TAnalyzer> where TAnalyzer : DiagnosticAnalyzer, new()
    {
        public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor) =>
            CSharpAnalyzerVerifier<TAnalyzer, DefaultVerifier>.Diagnostic(descriptor);

        public static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
        {
            var test = new Test { TestCode = source };
            test.ExpectedDiagnostics.AddRange(expected);
            await test.RunAsync(CancellationToken.None);
        }

        public static async Task VerifyNoDiagnosticsAsync(string source)
        {
            var test = new Test { TestCode = source };
            await test.RunAsync(CancellationToken.None);
        }

        private sealed class Test : CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            public Test()
            {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
                TestState.Sources.Add(Stubs.HttpClient);
                TestState.Sources.Add(Stubs.Polly);
            }
        }
    }
}
