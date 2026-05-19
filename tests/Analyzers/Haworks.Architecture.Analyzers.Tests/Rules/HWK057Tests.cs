using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK057Tests
{
    [Fact]
    public async Task WithOrigins_Wildcard_Reports()
    {
        const string source = """
            public class CorsPolicyBuilder
            {
                public CorsPolicyBuilder WithOrigins(params string[] origins) => this;
            }
            public class Startup
            {
                public void Configure()
                {
                    var builder = new CorsPolicyBuilder();
                    builder.WithOrigins({|#0:"*"|});
                }
            }
            """;
        var expected = CSharpAnalyzerVerifier<HWK057_NoCorsWildcardAnalyzer>
            .Diagnostic(Diagnostics.NoCorsWildcard)
            .WithLocation(0);
        await CSharpAnalyzerVerifier<HWK057_NoCorsWildcardAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task WithOrigins_SpecificDomain_NoDiagnostic()
    {
        const string source = """
            public class CorsPolicyBuilder
            {
                public CorsPolicyBuilder WithOrigins(params string[] origins) => this;
            }
            public class Startup
            {
                public void Configure()
                {
                    var builder = new CorsPolicyBuilder();
                    builder.WithOrigins("https://example.com");
                }
            }
            """;
        await CSharpAnalyzerVerifier<HWK057_NoCorsWildcardAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
