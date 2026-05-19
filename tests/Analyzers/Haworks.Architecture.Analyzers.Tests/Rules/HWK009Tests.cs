using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK009Tests
{
    [Fact]
    public async Task HttpClient_GetAsync_Inside_Transaction_Reports()
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
                    using var tx = await _db.Database.BeginTransactionAsync();
                    var result = await {|#0:_http.GetAsync("https://api.stripe.com/v1/charges")|};
                    await _db.SaveChangesAsync();
                }
            }
            """;

        var expected = WithHttpVerifier<HWK009_NoExternalIoInsideTransactionAnalyzer>
            .Diagnostic(Diagnostics.NoExternalIoInsideTransaction)
            .WithLocation(0)
            .WithArguments("GetAsync");

        await WithHttpVerifier<HWK009_NoExternalIoInsideTransactionAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task HttpClient_PostAsync_Inside_Transaction_Reports()
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
                    using var tx = await _db.Database.BeginTransactionAsync();
                    await _db.SaveChangesAsync();
                    var result = await {|#0:_http.PostAsync("https://api.stripe.com", new StringContent("{}"))|};
                }
            }
            """;

        var expected = WithHttpVerifier<HWK009_NoExternalIoInsideTransactionAnalyzer>
            .Diagnostic(Diagnostics.NoExternalIoInsideTransaction)
            .WithLocation(0)
            .WithArguments("PostAsync");

        await WithHttpVerifier<HWK009_NoExternalIoInsideTransactionAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task HttpClient_Outside_Transaction_NoDiagnostic()
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
                    using var tx = await _db.Database.BeginTransactionAsync();
                    await _db.SaveChangesAsync();
                }
            }
            """;

        await WithHttpVerifier<HWK009_NoExternalIoInsideTransactionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HttpClient_Before_Transaction_NoDiagnostic()
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
                    var result = await _http.GetAsync("https://api.example.com");
                    using var tx = await _db.Database.BeginTransactionAsync();
                    await _db.SaveChangesAsync();
                }
            }
            """;

        await WithHttpVerifier<HWK009_NoExternalIoInsideTransactionAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GatewayInterface_Inside_Transaction_Reports()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            public interface IPayoutGateway
            {
                Task<string> CreateConnectedAccountAsync(string email, CancellationToken ct);
            }
            public class Service
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                private readonly IPayoutGateway _gateway;
                public async Task DoWork(CancellationToken ct)
                {
                    using var tx = await _db.Database.BeginTransactionAsync(ct);
                    var id = await {|#0:_gateway.CreateConnectedAccountAsync("test@test.com", ct)|};
                    await _db.SaveChangesAsync(ct);
                }
            }
            """;

        var expected = WithHttpVerifier<HWK009_NoExternalIoInsideTransactionAnalyzer>
            .Diagnostic(Diagnostics.NoExternalIoInsideTransaction)
            .WithLocation(0)
            .WithArguments("CreateConnectedAccountAsync");

        await WithHttpVerifier<HWK009_NoExternalIoInsideTransactionAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    /// <summary>
    /// Verifier that includes the HttpClient stub in addition to the standard stubs.
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
