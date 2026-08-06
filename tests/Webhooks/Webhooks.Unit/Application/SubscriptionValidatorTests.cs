using FluentAssertions;
using Haworks.Webhooks.Application.Subscriptions;
using Haworks.Webhooks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Haworks.Webhooks.Unit.Application;

public class SubscriptionValidatorTests
{
    private readonly CreateWebhookSubscriptionValidator _createValidator;

    public SubscriptionValidatorTests()
    {
        var options = new DbContextOptionsBuilder<WebhooksDbContext>()
            .UseInMemoryDatabase(databaseName: $"Webhooks_Validator_{Guid.NewGuid()}")
            .Options;
        var db = new WebhooksDbContext(options);
        _createValidator = new CreateWebhookSubscriptionValidator(db);
    }

    [Fact]
    public async Task Create_With_Invalid_Url_Should_Fail()
    {
        // Arrange
        var command = new CreateWebhookSubscriptionCommand(Guid.NewGuid(), "invalid-url", ["order.created"], null, true);

        // Act
        var result = await _createValidator.ValidateAsync(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == "Url");
    }

    [Fact]
    public async Task Create_With_Empty_Events_Should_Fail()
    {
        // Arrange
        var command = new CreateWebhookSubscriptionCommand(Guid.NewGuid(), "https://example.com", [], null, true);

        // Act
        var result = await _createValidator.ValidateAsync(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == "Events");
    }
}

public class IsPrivateIpTests
{
    [Theory]
    [InlineData("10.0.0.1")]          // 10.0.0.0/8
    [InlineData("172.16.0.1")]        // 172.16.0.0/12
    [InlineData("172.31.255.255")]    // 172.16.0.0/12 upper bound
    [InlineData("192.168.1.1")]       // 192.168.0.0/16
    [InlineData("127.0.0.1")]         // loopback
    [InlineData("169.254.1.1")]       // link-local
    [InlineData("100.64.0.1")]        // CGNAT
    [InlineData("0.0.0.0")]           // 0.0.0.0/8
    [InlineData("224.0.0.1")]         // multicast
    [InlineData("::1")]               // IPv6 loopback
    [InlineData("fe80::1")]           // IPv6 link-local
    [InlineData("fc00::1")]           // IPv6 unique-local
    public void Should_Classify_Private_And_Internal_Addresses_As_Private(string ip)
    {
        CreateWebhookSubscriptionValidator.IsPrivateIp(ip).Should().BeTrue();
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.216.34")]
    [InlineData("2606:4700:4700::1111")]
    public void Should_Classify_Public_Addresses_As_Not_Private(string ip)
    {
        CreateWebhookSubscriptionValidator.IsPrivateIp(ip).Should().BeFalse();
    }

    [Fact]
    public async Task IsHostSafeAsync_Should_Reject_Literal_Private_Ip_Host()
    {
        (await CreateWebhookSubscriptionValidator.IsHostSafeAsync("169.254.169.254", CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public async Task IsHostSafeAsync_Should_Reject_Unresolvable_Host()
    {
        (await CreateWebhookSubscriptionValidator.IsHostSafeAsync("this-host-does-not-exist.invalid", CancellationToken.None))
            .Should().BeFalse();
    }
}
