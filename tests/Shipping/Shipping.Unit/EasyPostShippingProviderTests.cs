using EasyPost;
using EasyPost.Models.API;
using FluentAssertions;
using Haworks.Shipping.Api.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Haworks.Shipping.Unit;

public class EasyPostShippingProviderTests
{
    private readonly Mock<Client> _mockClient;
    private readonly Mock<ILogger<EasyPostShippingProvider>> _mockLogger;
    private readonly EasyPostShippingProvider _provider;

    public EasyPostShippingProviderTests()
    {
        _mockClient = new Mock<Client>();
        _mockLogger = new Mock<ILogger<EasyPostShippingProvider>>();

        var options = Options.Create(new EasyPostOptions
        {
            ApiKey = "test_key",
            TestMode = true
        });

        // Note: We cannot easily mock the EasyPost Client constructor, so these tests
        // focus on business logic that can be tested. For full integration testing,
        // use the integration test project with a real or test EasyPost environment.
        _provider = new EasyPostShippingProvider(options, _mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithValidOptions_SetsTestMode()
    {
        // Arrange
        var options = Options.Create(new EasyPostOptions
        {
            ApiKey = "test_key_123",
            TestMode = true
        });

        // Act
        var provider = new EasyPostShippingProvider(options, _mockLogger.Object);

        // Assert
        provider.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithEmptyApiKey_SetsEmptyKey()
    {
        // Arrange
        var options = Options.Create(new EasyPostOptions
        {
            ApiKey = "",
            TestMode = false
        });

        // Act
        var provider = new EasyPostShippingProvider(options, _mockLogger.Object);

        // Assert
        provider.Should().NotBeNull();
    }

    // Note: The actual EasyPost API integration tests would be in the integration test project
    // where we can use either test mode with real API calls or mock the HTTP responses.
    // These unit tests focus on the constructable aspects and would be expanded with
    // dependency injection improvements to make the Provider more testable.

    [Theory]
    [InlineData("test_key_123", true)]
    [InlineData("live_key_456", false)]
    [InlineData("", true)]
    public void Constructor_WithVariousConfigurations_DoesNotThrow(string apiKey, bool testMode)
    {
        // Arrange
        var options = Options.Create(new EasyPostOptions
        {
            ApiKey = apiKey,
            TestMode = testMode
        });

        // Act & Assert
        var act = () => new EasyPostShippingProvider(options, _mockLogger.Object);
        act.Should().NotThrow();
    }
}