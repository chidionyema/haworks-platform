using FluentAssertions;
using Xunit;

namespace Haworks.Tests.Smoke;

[Collection("Smoke Tests")]
public class ApiHealthSmokeTests(EnvironmentAgnosticFixture fixture)
{
    [Fact]
    public async Task Root_Endpoint_IsReachable()
    {
        // The BFF has no root handler — 404 is fine. A connection failure
        // would throw, so any HTTP status proves the server is reachable.
        var response = await fixture.HttpClient.GetAsync("/");
        ((int)response.StatusCode).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Health_Endpoint_ReturnsHealthy()
    {
        var response = await fixture.HttpClient.GetAsync("/health");
        response.IsSuccessStatusCode.Should().BeTrue();
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("Healthy");
    }
}
