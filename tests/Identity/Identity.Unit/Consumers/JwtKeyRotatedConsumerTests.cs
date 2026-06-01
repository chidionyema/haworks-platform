using Haworks.BuildingBlocks.Vault;
using Haworks.Contracts.Secrets;
using Haworks.Identity.Application.Consumers;
using Haworks.Identity.Application.Options;
using Haworks.Identity.Application.Services;
using Haworks.BuildingBlocks.Testing;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit.Abstractions;
using Xunit;

namespace Haworks.Identity.UnitTests.Consumers;

public class JwtKeyRotatedConsumerTests : TestBase
{
    private readonly Mock<IVaultService> _vaultServiceMock;
    private readonly Mock<DualKeyJwtValidator> _dualKeyValidatorMock;
    private readonly Mock<IJwtSigningKeyProvider> _signingKeyProviderMock;
    private readonly Mock<IOptionsMonitor<JwtOptions>> _jwtOptionsMock;
    private readonly Mock<ILogger<JwtKeyRotatedConsumer>> _loggerMock;
    private readonly JwtKeyRotatedConsumer _consumer;
    private readonly JwtOptions _jwtOptions;

    public JwtKeyRotatedConsumerTests(ITestOutputHelper output) : base(output)
    {
        _vaultServiceMock = new Mock<IVaultService>();
        _dualKeyValidatorMock = new Mock<DualKeyJwtValidator>();
        _signingKeyProviderMock = new Mock<IJwtSigningKeyProvider>();
        _jwtOptionsMock = new Mock<IOptionsMonitor<JwtOptions>>();
        _loggerMock = new Mock<ILogger<JwtKeyRotatedConsumer>>();

        _jwtOptions = new JwtOptions
        {
            OverlapMinutes = 30
        };
        _jwtOptionsMock.Setup(o => o.CurrentValue).Returns(_jwtOptions);

        _consumer = new JwtKeyRotatedConsumer(
            _vaultServiceMock.Object,
            _dualKeyValidatorMock.Object,
            _signingKeyProviderMock.Object,
            _jwtOptionsMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Consume_WithValidEvent_RefreshesVaultCredentialsAndSetsOverlapWindow()
    {
        // Arrange
        var rotationId = Guid.NewGuid();
        var currentKey = "current-signing-key";
        var @event = new JwtKeyRotatedEvent
        {
            RotationId = rotationId,
            Timestamp = DateTime.UtcNow
        };

        var contextMock = new Mock<ConsumeContext<JwtKeyRotatedEvent>>();
        contextMock.Setup(c => c.Message).Returns(@event);
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _signingKeyProviderMock.Setup(p => p.SigningKey).Returns(currentKey);
        _vaultServiceMock.Setup(v => v.RefreshCredentials("haworks-identity", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _consumer.Consume(contextMock.Object);

        // Assert
        _dualKeyValidatorMock.Verify(d => d.SetPreviousKey(currentKey, 30), Times.Once);
        _vaultServiceMock.Verify(v => v.RefreshCredentials("haworks-identity", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Consume_WithNullCurrentKey_StillRefreshesCredentials()
    {
        // Arrange
        var rotationId = Guid.NewGuid();
        var @event = new JwtKeyRotatedEvent
        {
            RotationId = rotationId,
            Timestamp = DateTime.UtcNow
        };

        var contextMock = new Mock<ConsumeContext<JwtKeyRotatedEvent>>();
        contextMock.Setup(c => c.Message).Returns(@event);
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _signingKeyProviderMock.Setup(p => p.SigningKey).Returns((string?)null);
        _vaultServiceMock.Setup(v => v.RefreshCredentials("haworks-identity", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _consumer.Consume(contextMock.Object);

        // Assert
        _dualKeyValidatorMock.Verify(d => d.SetPreviousKey(null, 30), Times.Once);
        _vaultServiceMock.Verify(v => v.RefreshCredentials("haworks-identity", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Consume_WhenVaultRefreshFails_PropagatesException()
    {
        // Arrange
        var rotationId = Guid.NewGuid();
        var @event = new JwtKeyRotatedEvent
        {
            RotationId = rotationId,
            Timestamp = DateTime.UtcNow
        };

        var contextMock = new Mock<ConsumeContext<JwtKeyRotatedEvent>>();
        contextMock.Setup(c => c.Message).Returns(@event);
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _signingKeyProviderMock.Setup(p => p.SigningKey).Returns("current-key");
        _vaultServiceMock.Setup(v => v.RefreshCredentials("haworks-identity", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Vault unavailable"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _consumer.Consume(contextMock.Object));

        // Verify overlap window was still set before the failure
        _dualKeyValidatorMock.Verify(d => d.SetPreviousKey("current-key", 30), Times.Once);
    }

    [Fact]
    public async Task Consume_LogsCorrectMessages()
    {
        // Arrange
        var rotationId = Guid.NewGuid();
        var @event = new JwtKeyRotatedEvent
        {
            RotationId = rotationId,
            Timestamp = DateTime.UtcNow
        };

        var contextMock = new Mock<ConsumeContext<JwtKeyRotatedEvent>>();
        contextMock.Setup(c => c.Message).Returns(@event);
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _signingKeyProviderMock.Setup(p => p.SigningKey).Returns("test-key");
        _vaultServiceMock.Setup(v => v.RefreshCredentials("haworks-identity", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _consumer.Consume(contextMock.Object);

        // Assert
        VerifyLogMessage(LogLevel.Information, $"Processing JwtKeyRotatedEvent {rotationId}");
        VerifyLogMessage(LogLevel.Information, $"JwtKeyRotatedEvent {rotationId} processed; overlap window active for 30m");
    }

    private void VerifyLogMessage(LogLevel level, string message)
    {
        _loggerMock.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(message)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}