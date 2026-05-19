using Haworks.Architecture.Analyzers.Rules;
using Haworks.Architecture.Analyzers.Tests.Verifiers;
using Xunit;

namespace Haworks.Architecture.Analyzers.Tests.Rules;

public class HWK055Tests
{
    [Fact]
    public async Task UnvalidatedUserUrl_InHttpCall_Reports()
    {
        const string source = """
            using System.Threading.Tasks;
            public class HttpClient
            {
                public Task<string> GetAsync(string url) => Task.FromResult("");
            }
            public class WebhookService
            {
                private readonly HttpClient _httpClient;
                public async Task Send(string webhookUrl)
                {
                    await _httpClient.GetAsync({|#0:webhookUrl|});
                }
            }
            """;
        var expected = CSharpAnalyzerVerifier<HWK055_NoUnvalidatedUserUrlsAnalyzer>
            .Diagnostic(Diagnostics.NoUnvalidatedUserUrls)
            .WithLocation(0)
            .WithArguments("webhookUrl");
        await CSharpAnalyzerVerifier<HWK055_NoUnvalidatedUserUrlsAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task ValidatedUrl_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            public class HttpClient
            {
                public Task<string> GetAsync(string url) => Task.FromResult("");
            }
            public class WebhookService
            {
                private readonly HttpClient _httpClient;
                public async Task Send(string webhookUrl)
                {
                    ValidateUrl(webhookUrl);
                    await _httpClient.GetAsync(webhookUrl);
                }
                private void ValidateUrl(string url) { }
            }
            """;
        await CSharpAnalyzerVerifier<HWK055_NoUnvalidatedUserUrlsAnalyzer>
            .VerifyNoDiagnosticsAsync(source);
    }
}
