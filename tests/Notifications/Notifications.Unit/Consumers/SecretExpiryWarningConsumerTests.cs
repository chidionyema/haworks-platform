using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Haworks.Contracts.Secrets;
using Haworks.Notifications.Application.Consumers;

namespace Haworks.Notifications.Unit.Consumers;

[Trait("Category", "Unit")]
public sealed class SecretExpiryWarningConsumerTests
{
    private readonly Mock<ILogger<SecretExpiryWarningConsumer>> _logger = new();
    private readonly SecretExpiryWarningConsumer _sut;

    public SecretExpiryWarningConsumerTests()
    {
        _sut = new SecretExpiryWarningConsumer(_logger.Object);
    }

    [Fact]
    public async Task Consume_SecretExpiryWarningEvent_LogsCriticalAlert()
    {
        // Arrange
        var secretPath = "secrets/payments/stripe-key";
        var lastRotated = DateTimeOffset.UtcNow.AddDays(-25);
        var agePercent = 0.83; // 83% of TTL elapsed

        var evt = new SecretExpiryWarningEvent
        {
            SecretPath = secretPath,
            AgePercent = agePercent,
            LastRotatedAt = lastRotated
        };

        var context = new Mock<ConsumeContext<SecretExpiryWarningEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);

        // Act
        await _sut.Consume(context.Object);

        // Assert
        _logger.Verify(
            l => l.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("ROTATION ALERT")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Verify the log message contains critical information
        _logger.Verify(
            l => l.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains(secretPath) &&
                    state.ToString()!.Contains("83%") &&
                    state.ToString()!.Contains("Immediate rotation required")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_HighAgePercentage_LogsWithCorrectPercentageFormat()
    {
        // Arrange
        var evt = new SecretExpiryWarningEvent
        {
            SecretPath = "secrets/db/postgres-password",
            AgePercent = 0.95, // 95% of TTL elapsed - critical
            LastRotatedAt = DateTimeOffset.UtcNow.AddDays(-28)
        };

        var context = new Mock<ConsumeContext<SecretExpiryWarningEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);

        // Act
        await _sut.Consume(context.Object);

        // Assert - verify percentage is formatted correctly (95%)
        _logger.Verify(
            l => l.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("95%")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_VaultPath_IncludedInLogMessage()
    {
        // Arrange
        var vaultPath = "secrets/external/auth0-client-secret";
        var evt = new SecretExpiryWarningEvent
        {
            SecretPath = vaultPath,
            AgePercent = 0.80,
            LastRotatedAt = DateTimeOffset.UtcNow.AddDays(-24)
        };

        var context = new Mock<ConsumeContext<SecretExpiryWarningEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);

        // Act
        await _sut.Consume(context.Object);

        // Assert - verify the specific vault path is logged
        _logger.Verify(
            l => l.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(vaultPath)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_CompletesSuccessfully()
    {
        // Arrange
        var evt = new SecretExpiryWarningEvent
        {
            SecretPath = "secrets/api/external-service-key",
            AgePercent = 0.85,
            LastRotatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };

        var context = new Mock<ConsumeContext<SecretExpiryWarningEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);

        // Act & Assert - should complete without throwing
        var act = async () => await _sut.Consume(context.Object);
        await act.Should().NotThrowAsync();
    }
}