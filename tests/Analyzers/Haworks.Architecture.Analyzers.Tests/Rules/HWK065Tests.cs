using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK065Tests
{
    [Fact]
    public async Task QueryHandler_Without_AsNoTracking_Reports()
    {
        const string source = """
            using System.Threading.Tasks;
            public class DbSet<T> where T : class
            {
                public Task<System.Collections.Generic.List<T>> ToListAsync() => null;
            }
            public class {|#0:GetOrderHandler|}
            {
                private readonly DbSet<object> _orders;
                public async Task Handle()
                {
                    var result = await _orders.ToListAsync();
                }
            }
            """;
        var expected = CSharpAnalyzerVerifier<HWK065_NoTrackedQueriesInReadHandlersAnalyzer>
            .Diagnostic(Diagnostics.NoTrackedQueriesInReadHandlers)
            .WithLocation(0)
            .WithArguments("GetOrderHandler");
        await CSharpAnalyzerVerifier<HWK065_NoTrackedQueriesInReadHandlersAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task QueryHandler_With_AsNoTracking_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            public class DbSet<T> where T : class
            {
                public DbSet<T> AsNoTracking() => this;
                public Task<System.Collections.Generic.List<T>> ToListAsync() => null;
            }
            public class GetOrderHandler
            {
                private readonly DbSet<object> _orders;
                public async Task Handle()
                {
                    var result = await _orders.AsNoTracking().ToListAsync();
                }
            }
            """;
        await CSharpAnalyzerVerifier<HWK065_NoTrackedQueriesInReadHandlersAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task CommandHandler_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            public class CreateOrderHandler
            {
                private readonly Microsoft.EntityFrameworkCore.DbContext _db;
                public async Task Handle()
                {
                    await _db.SaveChangesAsync();
                }
            }
            """;
        await CSharpAnalyzerVerifier<HWK065_NoTrackedQueriesInReadHandlersAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
