using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK063Tests
{
    [Fact]
    public async Task AddSingleton_DbContext_Reports()
    {
        const string source = """
            public interface IServiceCollection
            {
                void AddSingleton<T>() where T : class;
            }
            public class OrderDbContext { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddSingleton<OrderDbContext>()|};
                }
            }
            """;
        var expected = CSharpAnalyzerVerifier<HWK063_NoDbContextSingletonAnalyzer>
            .Diagnostic(Diagnostics.NoDbContextSingleton)
            .WithLocation(0)
            .WithArguments("OrderDbContext");
        await CSharpAnalyzerVerifier<HWK063_NoDbContextSingletonAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AddScoped_DbContext_NoDiagnostic()
    {
        const string source = """
            public interface IServiceCollection
            {
                void AddScoped<T>() where T : class;
            }
            public class OrderDbContext { }
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<OrderDbContext>();
                }
            }
            """;
        await CSharpAnalyzerVerifier<HWK063_NoDbContextSingletonAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
