using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK080Tests
{
    [Fact]
    public async Task SaveChangesAsync_Inside_Foreach_Reports()
    {
        const string source = """
            using System.Threading.Tasks;
            using System.Collections.Generic;
            public class Service
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                public async Task Process(List<string> items)
                {
                    foreach (var item in items)
                    {
                        await {|#0:_db.SaveChangesAsync()|};
                    }
                }
            }
            """;

        var expected = Verifier<HWK080_NoSaveChangesInLoopAnalyzer>
            .Diagnostic(Diagnostics.NoSaveChangesInLoop)
            .WithLocation(0)
            .WithArguments("foreach");

        await Verifier<HWK080_NoSaveChangesInLoopAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task SaveChangesAsync_Inside_For_Reports()
    {
        const string source = """
            using System.Threading.Tasks;
            public class Service
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                public async Task Process()
                {
                    for (int i = 0; i < 10; i++)
                    {
                        await {|#0:_db.SaveChangesAsync()|};
                    }
                }
            }
            """;

        var expected = Verifier<HWK080_NoSaveChangesInLoopAnalyzer>
            .Diagnostic(Diagnostics.NoSaveChangesInLoop)
            .WithLocation(0)
            .WithArguments("for");

        await Verifier<HWK080_NoSaveChangesInLoopAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task SaveChangesAsync_Outside_Loop_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using System.Collections.Generic;
            public class Service
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                public async Task Process(List<string> items)
                {
                    foreach (var item in items)
                    {
                        // batch mutations
                    }
                    await _db.SaveChangesAsync();
                }
            }
            """;

        await Verifier<HWK080_NoSaveChangesInLoopAnalyzer>
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
                TestState.Sources.Add(Stubs.EfCore);
            }
        }
    }
}
