using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PostHog;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Haworks.PostHog.Integration;

public class PostHogWiringTests : IAsyncLifetime
{
    private WireMockServer _server = null!;
    private ServiceProvider _sp = null!;

    public Task InitializeAsync()
    {
        _server = WireMockServer.Start();

        // PostHog SDK POSTs to /batch for event capture and /decide for feature flags
        _server.Given(Request.Create().WithPath("/batch").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"status":1}"""));

        _server.Given(Request.Create().WithPath("/decide/").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""
            {
                "featureFlags": {"test-flag": true, "variant-flag": "control"},
                "featureFlagPayloads": {}
            }
            """));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PostHog:ProjectToken"] = "phc_test_token_for_integration",
                ["PostHog:HostUrl"] = _server.Url!,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        BuildingBlocks.PostHog.PostHogExtensions.AddHaworksPostHog(services, config);
        _sp = services.BuildServiceProvider();

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_sp is not null)
            await _sp.DisposeAsync();
        _server?.Stop();
    }

    [Fact]
    public void AddHaworksPostHog_Registers_PostHog_Client()
    {
        var client = _sp.GetService<global::PostHog.IPostHogClient>();
        client.Should().NotBeNull("AddHaworksPostHog should register IPostHogClient");
    }

    [Fact]
    public async Task PostHog_Client_Can_Capture_Event()
    {
        var client = _sp.GetRequiredService<global::PostHog.IPostHogClient>();

        client.Capture("user-123", "test_event");

        // PostHog SDK batches events — flush by disposing
        await _sp.DisposeAsync();

        // Verify WireMock received the /batch POST
        var requests = _server.FindLogEntries(Request.Create().WithPath("/batch").UsingPost());
        requests.Should().NotBeEmpty("PostHog SDK should POST captured events to /batch");
    }
}
