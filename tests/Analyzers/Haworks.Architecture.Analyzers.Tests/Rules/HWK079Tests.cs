using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK079Tests
{
    [Fact]
    public async Task ConfigureAwaitFalse_In_AspNet_Project_Reports()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Mvc;
            public class OrdersController : Controller
            {
                public async Task<OkResult> Get()
                {
                    await {|#0:Task.Delay(1).ConfigureAwait(false)|};
                    return Ok();
                }
            }
            """;

        var expected = Verifier<HWK079_NoConfigureAwaitFalseInAspNetAnalyzer>
            .Diagnostic(Diagnostics.NoConfigureAwaitFalseInAspNet)
            .WithLocation(0);

        await Verifier<HWK079_NoConfigureAwaitFalseInAspNetAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task ConfigureAwaitTrue_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Mvc;
            public class OrdersController : Controller
            {
                public async Task<OkResult> Get()
                {
                    await Task.Delay(1).ConfigureAwait(true);
                    return Ok();
                }
            }
            """;

        await Verifier<HWK079_NoConfigureAwaitFalseInAspNetAnalyzer>
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
