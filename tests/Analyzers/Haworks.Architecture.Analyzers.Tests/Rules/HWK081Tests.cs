using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK081Tests
{
    [Fact]
    public async Task GetRequiredService_In_Method_Reports()
    {
        const string source = """
            using System;
            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceProviderServiceExtensions
                {
                    public static T GetRequiredService<T>(this IServiceProvider provider) => default;
                }
            }
            public class MyService
            {
                private readonly IServiceProvider _sp;
                public void DoWork()
                {
                    var svc = {|#0:Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<string>(_sp)|};
                }
            }
            """;

        var expected = Verifier<HWK081_NoServiceLocatorInSingletonAnalyzer>
            .Diagnostic(Diagnostics.NoServiceLocatorInSingleton)
            .WithLocation(0)
            .WithArguments("GetRequiredService in DoWork");

        await Verifier<HWK081_NoServiceLocatorInSingletonAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task GetService_In_DI_Lambda_NoDiagnostic()
    {
        const string source = """
            using System;
            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceProviderServiceExtensions
                {
                    public static T GetRequiredService<T>(this IServiceProvider provider) => default;
                }
            }
            public interface IServiceCollection
            {
                void AddScoped<T>(Func<IServiceProvider, T> factory);
            }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<string>(sp => Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<string>(sp));
                }
            }
            """;

        await Verifier<HWK081_NoServiceLocatorInSingletonAnalyzer>
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
            }
        }
    }
}
