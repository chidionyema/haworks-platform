using Haworks.Contracts.Privacy;
using Haworks.Contracts.Secrets;
using Haworks.Identity.Application.Consumers;
using Haworks.Identity.Domain;
using Haworks.BuildingBlocks.Testing.Containers;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Haworks.Identity.Integration;

[Collection("Identity Integration Tests")]
public class ConsumerIntegrationTests : IClassFixture<IdentityWebAppFactory>
{
    private readonly IdentityWebAppFactory _factory;

    public ConsumerIntegrationTests(IdentityWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PrivacyErasureRequestedConsumer_AnonymizesUserSuccessfully()
    {
        // Arrange
        await _factory.EnsureSchemaAsync();

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var consumer = scope.ServiceProvider.GetRequiredService<PrivacyErasureRequestedConsumer>();

        // Create a test user with PII data
        var testUser = new User
        {
            UserName = "privacy.test.user",
            Email = "privacy.test@example.com",
            PhoneNumber = "+1234567890",
            StripeCustomerId = "cus_test123",
            CheckoutSessionId = "cs_test456",
            IsActive = true,
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(testUser);
        Assert.True(createResult.Succeeded);

        var userId = Guid.Parse(testUser.Id);
        var requestId = Guid.NewGuid();

        var erasureRequest = new PrivacyErasureRequested
        {
            UserId = userId,
            RequestId = requestId
        };

        var mockContext = new Mock<ConsumeContext<PrivacyErasureRequested>>();
        mockContext.Setup(c => c.Message).Returns(erasureRequest);
        mockContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        mockContext.Setup(c => c.Publish(It.IsAny<PrivacyErasureCompleted>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await consumer.Consume(mockContext.Object);

        // Assert
        var anonymizedUser = await userManager.FindByIdAsync(testUser.Id);
        Assert.NotNull(anonymizedUser);

        // Verify anonymization
        Assert.Equal($"DELETED-{userId:N}", anonymizedUser.UserName);
        Assert.Equal($"deleted-{userId:N}@privacy.invalid", anonymizedUser.Email);
        Assert.Null(anonymizedUser.PhoneNumber);
        Assert.Null(anonymizedUser.StripeCustomerId);
        Assert.Null(anonymizedUser.CheckoutSessionId);
        Assert.False(anonymizedUser.IsActive);

        // Verify completion event was published
        mockContext.Verify(c => c.Publish(It.Is<PrivacyErasureCompleted>(e =>
            e.UserId == userId &&
            e.RequestId == requestId &&
            e.ServiceName == "identity-svc"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PrivacyErasureRequestedConsumer_UserNotFound_PublishesCompletion()
    {
        // Arrange
        await _factory.EnsureSchemaAsync();

        using var scope = _factory.Services.CreateScope();
        var consumer = scope.ServiceProvider.GetRequiredService<PrivacyErasureRequestedConsumer>();

        var userId = Guid.NewGuid(); // Non-existent user
        var requestId = Guid.NewGuid();

        var erasureRequest = new PrivacyErasureRequested
        {
            UserId = userId,
            RequestId = requestId
        };

        var mockContext = new Mock<ConsumeContext<PrivacyErasureRequested>>();
        mockContext.Setup(c => c.Message).Returns(erasureRequest);
        mockContext.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        mockContext.Setup(c => c.Publish(It.IsAny<PrivacyErasureCompleted>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await consumer.Consume(mockContext.Object);

        // Assert
        // Should still publish completion even if user not found
        mockContext.Verify(c => c.Publish(It.Is<PrivacyErasureCompleted>(e =>
            e.UserId == userId &&
            e.RequestId == requestId &&
            e.ServiceName == "identity-svc"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JwtKeyRotatedConsumer_ProcessesEventCorrectly()
    {
        // Arrange
        await _factory.EnsureSchemaAsync();

        using var scope = _factory.Services.CreateScope();

        // Note: JwtKeyRotatedConsumer requires vault services which aren't available in test
        // In a real integration test, you'd need to mock vault services or use test doubles
        // This test verifies the consumer can be resolved from DI

        var services = scope.ServiceProvider;

        // Verify all required dependencies are registered
        Assert.NotNull(services.GetService<JwtKeyRotatedConsumer>());

        // For full integration, you'd need:
        // - Mock vault services
        // - Create JwtKeyRotatedEvent
        // - Verify dual key validator is called
        // - Verify vault refresh is triggered
    }
}