using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK050Tests
{
    [Fact]
    public async Task AsyncCall_WithoutCT_WhenAvailable_Reports()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            public class Db
            {
                public Task<int> SaveChangesAsync() => Task.FromResult(0);
                public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);
            }
            public class Service
            {
                private readonly Db _db = new();
                public async Task DoWork(CancellationToken ct)
                {
                    await {|#0:_db.SaveChangesAsync()|};
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<HWK050_MustPropagateCancellationTokenAnalyzer>
            .Diagnostic(Diagnostics.MustPropagateCancellationToken)
            .WithLocation(0)
            .WithArguments("SaveChangesAsync", "ct");

        await CSharpAnalyzerVerifier<HWK050_MustPropagateCancellationTokenAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AsyncCall_WithCT_NoDiagnostic()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            public class Db
            {
                public Task<int> SaveChangesAsync() => Task.FromResult(0);
                public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);
            }
            public class Service
            {
                private readonly Db _db = new();
                public async Task DoWork(CancellationToken ct)
                {
                    await _db.SaveChangesAsync(ct);
                }
            }
            """;

        await CSharpAnalyzerVerifier<HWK050_MustPropagateCancellationTokenAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task AsyncCall_NoCTInScope_NoDiagnostic()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            public class Db
            {
                public Task<int> SaveChangesAsync() => Task.FromResult(0);
                public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);
            }
            public class Service
            {
                private readonly Db _db = new();
                public async Task DoWork()
                {
                    await _db.SaveChangesAsync();
                }
            }
            """;

        await CSharpAnalyzerVerifier<HWK050_MustPropagateCancellationTokenAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
