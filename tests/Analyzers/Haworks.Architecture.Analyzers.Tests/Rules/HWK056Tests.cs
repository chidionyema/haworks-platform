using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK056Tests
{
    [Fact]
    public async Task AllowAnyOrigin_With_AllowCredentials_Reports()
    {
        const string source = """
            public class CorsPolicy
            {
                public CorsPolicy AllowAnyOrigin() => this;
                public CorsPolicy AllowCredentials() => this;
            }
            public class Startup
            {
                public void Configure()
                {
                    var action = {|#0:(CorsPolicy p) => p.AllowAnyOrigin().AllowCredentials()|};
                }
            }
            """;
        var expected = CSharpAnalyzerVerifier<HWK056_NoAllowAnyOriginWithCredentialsAnalyzer>
            .Diagnostic(Diagnostics.NoAllowAnyOriginWithCredentials)
            .WithLocation(0);
        await CSharpAnalyzerVerifier<HWK056_NoAllowAnyOriginWithCredentialsAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AllowAnyOrigin_Without_Credentials_NoDiagnostic()
    {
        const string source = """
            public class CorsPolicy
            {
                public CorsPolicy AllowAnyOrigin() => this;
                public CorsPolicy AllowAnyMethod() => this;
            }
            public class Startup
            {
                public void Configure()
                {
                    var action = (CorsPolicy p) => p.AllowAnyOrigin().AllowAnyMethod();
                }
            }
            """;
        await CSharpAnalyzerVerifier<HWK056_NoAllowAnyOriginWithCredentialsAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
