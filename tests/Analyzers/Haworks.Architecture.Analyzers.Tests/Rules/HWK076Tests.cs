using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK076Tests
{
    [Fact]
    public async Task TaskRun_Inside_Controller_Reports()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Mvc;
            public class OrdersController : Controller
            {
                public async Task<OkResult> Create()
                {
                    {|#0:Task.Run(() => { })|};
                    return Ok();
                }
            }
            """;

        var expected = Verifier<HWK076_NoTaskRunInRequestPipelineAnalyzer>
            .Diagnostic(Diagnostics.NoTaskRunInRequestPipeline)
            .WithLocation(0)
            .WithArguments("OrdersController");

        await Verifier<HWK076_NoTaskRunInRequestPipelineAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task TaskRun_Inside_Middleware_Reports()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            public class LoggingMiddleware : IMiddleware
            {
                public async Task InvokeAsync(HttpContext context, RequestDelegate next)
                {
                    {|#0:Task.Run(() => { })|};
                    await next(context);
                }
            }
            """;

        var expected = Verifier<HWK076_NoTaskRunInRequestPipelineAnalyzer>
            .Diagnostic(Diagnostics.NoTaskRunInRequestPipeline)
            .WithLocation(0)
            .WithArguments("LoggingMiddleware");

        await Verifier<HWK076_NoTaskRunInRequestPipelineAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task TaskRun_In_Plain_Service_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            public class BackgroundProcessor
            {
                public void Process()
                {
                    Task.Run(() => { });
                }
            }
            """;

        await Verifier<HWK076_NoTaskRunInRequestPipelineAnalyzer>
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
                TestState.Sources.Add(Stubs.AspNetCore);
            }
        }
    }
}
