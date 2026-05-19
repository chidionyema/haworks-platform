using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK075Tests
{
    [Fact]
    public async Task SaveChanges_HttpCall_SaveChanges_Without_Separate_Transactions_Reports()
    {
        const string source = """
            using System.Threading.Tasks;
            using System.Net.Http;
            public class Service
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                private readonly HttpClient _http;
                public async Task DoWork()
                {
                    await _db.SaveChangesAsync();
                    var result = await {|#0:_http.GetAsync("https://api.stripe.com")|};
                    await _db.SaveChangesAsync();
                }
            }
            """;

        var expected = WithHttpVerifier<HWK075_NoDbWriteExternalIoDbWriteSandwichAnalyzer>
            .Diagnostic(Diagnostics.DbWriteExternalIoDbWriteSandwich)
            .WithLocation(0)
            .WithArguments("9", "GetAsync", "10", "11");

        await WithHttpVerifier<HWK075_NoDbWriteExternalIoDbWriteSandwichAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task SaveChanges_GatewayCall_SaveChanges_Without_Separate_Transactions_Reports()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            public interface IPayoutGateway
            {
                Task<string> InitiatePayoutAsync(string id, CancellationToken ct);
            }
            public class Service
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                private readonly IPayoutGateway _gateway;
                public async Task DoWork(CancellationToken ct)
                {
                    await _db.SaveChangesAsync(ct);
                    var result = await {|#0:_gateway.InitiatePayoutAsync("seller-1", ct)|};
                    await _db.SaveChangesAsync(ct);
                }
            }
            """;

        var expected = WithHttpVerifier<HWK075_NoDbWriteExternalIoDbWriteSandwichAnalyzer>
            .Diagnostic(Diagnostics.DbWriteExternalIoDbWriteSandwich)
            .WithLocation(0)
            .WithArguments("13", "InitiatePayoutAsync", "14", "15");

        await WithHttpVerifier<HWK075_NoDbWriteExternalIoDbWriteSandwichAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task ThreePhase_Pattern_With_Separate_Transactions_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using System.Net.Http;
            public class Service
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                private readonly HttpClient _http;
                public async Task DoWork()
                {
                    // Phase 1: lock + pending + commit
                    using (var tx1 = await _db.Database.BeginTransactionAsync())
                    {
                        await _db.SaveChangesAsync();
                    }

                    // Phase 2: external I/O (no DB locks)
                    var result = await _http.GetAsync("https://api.stripe.com");

                    // Phase 3: re-lock + settle + commit
                    using (var tx2 = await _db.Database.BeginTransactionAsync())
                    {
                        await _db.SaveChangesAsync();
                    }
                }
            }
            """;

        await WithHttpVerifier<HWK075_NoDbWriteExternalIoDbWriteSandwichAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Single_SaveChanges_With_HttpCall_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using System.Net.Http;
            public class Service
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                private readonly HttpClient _http;
                public async Task DoWork()
                {
                    var result = await _http.GetAsync("https://api.stripe.com");
                    await _db.SaveChangesAsync();
                }
            }
            """;

        await WithHttpVerifier<HWK075_NoDbWriteExternalIoDbWriteSandwichAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task Two_SaveChanges_Without_ExternalIo_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            public class Service
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                public async Task DoWork()
                {
                    await _db.SaveChangesAsync();
                    await _db.SaveChangesAsync();
                }
            }
            """;

        await WithHttpVerifier<HWK075_NoDbWriteExternalIoDbWriteSandwichAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    /// <summary>
    /// Verifier that includes the HttpClient stub.
    /// </summary>
    private static class WithHttpVerifier<TAnalyzer>
        where TAnalyzer : DiagnosticAnalyzer, new()
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
                TestState.Sources.Add(Stubs.MassTransit);
                TestState.Sources.Add(Stubs.EfCore);
                TestState.Sources.Add(Stubs.Polly);
                TestState.Sources.Add(Stubs.HttpClient);
            }
        }
    }
}
