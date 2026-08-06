using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Haworks.Webhooks.Application.Subscriptions;
using Xunit;

namespace Haworks.Webhooks.Integration;

[Collection("Integration Tests")]
public class SubscriptionIntegrationTests(WebhooksWebAppFactory factory) : IClassFixture<WebhooksWebAppFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly Guid TestPartnerId = Guid.Parse(WebhooksTestAuthHandler.TestPartnerIdString);

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_Subscription_Should_Persist_To_Database()
    {
        // Arrange
        var request = new CreateWebhookSubscriptionRequest(
            Url: "https://partner.com/webhook",
            Events: ["order.created"],
            Secret: "super-secret"
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/webhooks/subscriptions", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreateWebhookSubscriptionResult>();
        created.Should().NotBeNull();
        created!.Id.Should().NotBeEmpty();

        var getResponse = await _client.GetAsync($"/api/v1/webhooks/subscriptions/{created.Id}");
        getResponse.EnsureSuccessStatusCode();
        var sub = await getResponse.Content.ReadFromJsonAsync<WebhookSubscriptionDto>();
        
        sub.Should().NotBeNull();
        sub!.PartnerId.Should().Be(TestPartnerId);
        sub.Url.Should().Be(request.Url);
        sub.Events.Should().BeEquivalentTo(request.Events);
        sub.SecretPreview.Should().Be("cret"); // last 4 chars of super-secret
    }
}
