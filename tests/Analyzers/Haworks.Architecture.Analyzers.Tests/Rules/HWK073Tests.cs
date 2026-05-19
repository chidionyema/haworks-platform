using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK073Tests
{
    [Fact]
    public async Task SaveChangesAsync_InsidePollyExecute_Reports()
    {
        const string source = """
            using System.Threading.Tasks;
            using Polly;
            public class Service
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                public async Task CallApi(IAsyncPolicy policy)
                {
                    await policy.ExecuteAsync<int>(async () =>
                    {
                        await {|#0:_db.SaveChangesAsync()|};
                        return 0;
                    });
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<HWK073_NoSideEffectsInPollyRetryAnalyzer>
            .Diagnostic(Diagnostics.SideEffectsInPollyRetry)
            .WithLocation(0)
            .WithArguments("SaveChangesAsync");

        await CSharpAnalyzerVerifier<HWK073_NoSideEffectsInPollyRetryAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task MassTransitPublish_InsidePollyExecute_Reports()
    {
        const string source = """
            using System.Threading.Tasks;
            using Polly;
            using MassTransit;
            public class MyEvent { }
            public class Service
            {
                private readonly ConsumeContext<MyEvent> _context;
                public async Task CallApi(IAsyncPolicy policy)
                {
                    await policy.ExecuteAsync<int>(async () =>
                    {
                        await {|#0:_context.Publish<MyEvent>(new MyEvent())|};
                        return 0;
                    });
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<HWK073_NoSideEffectsInPollyRetryAnalyzer>
            .Diagnostic(Diagnostics.SideEffectsInPollyRetry)
            .WithLocation(0)
            .WithArguments("Publish");

        await CSharpAnalyzerVerifier<HWK073_NoSideEffectsInPollyRetryAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task ExternalHttpCall_InsidePollyExecute_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using Polly;
            public class Service
            {
                public async Task<string> CallApi(IAsyncPolicy policy)
                {
                    return await policy.ExecuteAsync<string>(async () =>
                    {
                        await Task.Delay(1);
                        return "ok";
                    });
                }
            }
            """;

        await CSharpAnalyzerVerifier<HWK073_NoSideEffectsInPollyRetryAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task SaveChangesAsync_OutsidePollyExecute_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            public class Service
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                public async Task DoWork()
                {
                    await _db.SaveChangesAsync();
                }
            }
            """;

        await CSharpAnalyzerVerifier<HWK073_NoSideEffectsInPollyRetryAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
