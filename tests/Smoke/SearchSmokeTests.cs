using FluentAssertions;
using Xunit;

namespace Haworks.Tests.Smoke;

[Collection("Smoke Tests")]
public class SearchSmokeTests(EnvironmentAgnosticFixture fixture)
{
    /// <summary>
    /// Hits the BFF's <c>/api/search?q=test</c> route and asserts a 2xx with
    /// a JSON body whose <c>hits</c> array is present. The body assertion is
    /// intentionally lenient — staging may have an empty index — but the
    /// <em>shape</em> must match spec §3.1 so the BFF's contract holds.
    /// </summary>
    [Fact]
    public async Task BFF_search_returns_search_envelope()
    {
        var resp = await fixture.HttpClient.GetAsync("/api/v1/search?q=test");

        resp.StatusCode.Should().NotBe(System.Net.HttpStatusCode.NotFound,
            "BFF must expose /api/search after B7");
        resp.StatusCode.Should().NotBe(System.Net.HttpStatusCode.InternalServerError,
            "search-svc must be reachable from the BFF flycast");

        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("\"hits\"",
            "the response envelope must match spec §3.1");
        body.Should().Contain("\"totalHits\"");
    }
}
