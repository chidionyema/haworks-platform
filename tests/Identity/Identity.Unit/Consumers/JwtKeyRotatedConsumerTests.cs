using Haworks.BuildingBlocks.Vault;
using Haworks.Contracts.Secrets;
using Haworks.Identity.Application.Consumers;
using Haworks.Identity.Application.Options;
using Haworks.Identity.Application.Services;
using Haworks.BuildingBlocks.Testing;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
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
    private readonly Mock<ConsumeContext<JwtKeyRotatedEvent>> _contextMock;
    private readonly JwtKeyRotatedConsumer _consumer;

    public JwtKeyRotatedConsumerTests(ITestOutputHelper output) : base(output)
    {
        _vaultServiceMock = new Mock<IVaultService>();
        _dualKeyValidatorMock = new Mock<DualKeyJwtValidator>();
        _signingKeyProviderMock = new Mock<IJwtSigningKeyProvider>();
        _jwtOptionsMock = new Mock<IOptionsMonitor<JwtOptions>>();
        _loggerMock = new Mock<ILogger<JwtKeyRotatedConsumer>>();
        _contextMock = new Mock<ConsumeContext<JwtKeyRotatedEvent>>();

        var jwtOptions = new JwtOptions
        {
            OverlapMinutes = 30
        };

        _jwtOptionsMock
            .Setup(o => o.CurrentValue)
            .Returns(jwtOptions);

        _consumer = new JwtKeyRotatedConsumer(
            _vaultServiceMock.Object,
            _dualKeyValidatorMock.Object,
            _signingKeyProviderMock.Object,
            _jwtOptionsMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Consume_ValidEvent_PreservesCurrentKeyAsPrevious()
    {
        // Arrange
        var rotationId = Guid.NewGuid();
        var jwtKeyRotatedEvent = new JwtKeyRotatedEvent
        {
            RotationId = rotationId,
            RotatedAt = DateTimeOffset.UtcNow
        };

        _contextMock
            .Setup(c => c.Message)
            .Returns(jwtKeyRotatedEvent);

        _contextMock
            .Setup(c => c.CancellationToken)
            .Returns(CancellationToken.None);

        var currentKey = CreateMockSecurityKey();
        _signingKeyProviderMock
            .Setup(p => p.SigningKey)
            .Returns(currentKey);

        _vaultServiceMock
            .Setup(v => v.RefreshCredentials("haworks-identity", CancellationToken.None))
            .Returns(Task.CompletedTask);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        _dualKeyValidatorMock.Verify(v => v.SetPreviousKey(It.IsAny<SecurityKey>(), 30), Times.Once);
    }

    [Fact]
    public async Task Consume_ValidEvent_RefreshesVaultCredentials()
    {
        // Arrange
        var rotationId = Guid.NewGuid();
        var jwtKeyRotatedEvent = new JwtKeyRotatedEvent
        {
            RotationId = rotationId,
            RotatedAt = DateTimeOffset.UtcNow
        };

        _contextMock
            .Setup(c => c.Message)
            .Returns(jwtKeyRotatedEvent);

        _contextMock
            .Setup(c => c.CancellationToken)
            .Returns(CancellationToken.None);

        var currentKey = CreateMockSecurityKey();
        _signingKeyProviderMock
            .Setup(p => p.SigningKey)
            .Returns(currentKey);

        _vaultServiceMock
            .Setup(v => v.RefreshCredentials("haworks-identity", CancellationToken.None))
            .Returns(Task.CompletedTask);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        _vaultServiceMock.Verify(v => v.RefreshCredentials("haworks-identity", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Consume_ValidEvent_LogsProcessingMessages()
    {
        // Arrange
        var rotationId = Guid.NewGuid();
        var jwtKeyRotatedEvent = new JwtKeyRotatedEvent
        {
            RotationId = rotationId,
            RotatedAt = DateTimeOffset.UtcNow
        };

        _contextMock
            .Setup(c => c.Message)
            .Returns(jwtKeyRotatedEvent);

        _contextMock
            .Setup(c => c.CancellationToken)
            .Returns(CancellationToken.None);

        var currentKey = CreateMockSecurityKey();
        _signingKeyProviderMock
            .Setup(p => p.SigningKey)
            .Returns(currentKey);

        _vaultServiceMock
            .Setup(v => v.RefreshCredentials("haworks-identity", CancellationToken.None))
            .Returns(Task.CompletedTask);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Processing JwtKeyRotatedEvent")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("processed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Consume_VaultRefreshThrows_PropagatesException()
    {
        // Arrange
        var rotationId = Guid.NewGuid();
        var jwtKeyRotatedEvent = new JwtKeyRotatedEvent
        {
            RotationId = rotationId,
            RotatedAt = DateTimeOffset.UtcNow
        };

        _contextMock
            .Setup(c => c.Message)
            .Returns(jwtKeyRotatedEvent);

        _contextMock
            .Setup(c => c.CancellationToken)
            .Returns(CancellationToken.None);

        var currentKey = CreateMockSecurityKey();
        _signingKeyProviderMock
            .Setup(p => p.SigningKey)
            .Returns(currentKey);

        _vaultServiceMock
            .Setup(v => v.RefreshCredentials("haworks-identity", CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("Vault error"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _consumer.Consume(_contextMock.Object));

        // Verify the previous key was still set before the exception
        _dualKeyValidatorMock.Verify(v => v.SetPreviousKey(It.IsAny<SecurityKey>(), 30), Times.Once);
    }

    [Fact]
    public async Task Consume_CancellationRequested_PassesCancellationToVault()
    {
        // Arrange
        var rotationId = Guid.NewGuid();
        var jwtKeyRotatedEvent = new JwtKeyRotatedEvent
        {
            RotationId = rotationId,
            RotatedAt = DateTimeOffset.UtcNow
        };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _contextMock
            .Setup(c => c.Message)
            .Returns(jwtKeyRotatedEvent);

        _contextMock
            .Setup(c => c.CancellationToken)
            .Returns(cts.Token);

        var currentKey = CreateMockSecurityKey();
        _signingKeyProviderMock
            .Setup(p => p.SigningKey)
            .Returns(currentKey);

        _vaultServiceMock
            .Setup(v => v.RefreshCredentials("haworks-identity", cts.Token))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => _consumer.Consume(_contextMock.Object));

        // Verify cancellation token was passed
        _vaultServiceMock.Verify(v => v.RefreshCredentials("haworks-identity", cts.Token), Times.Once);
    }

    [Fact]
    public async Task Consume_CustomOverlapMinutes_UsesConfiguredValue()
    {
        // Arrange
        var customOverlapMinutes = 60;
        var jwtOptions = new JwtOptions
        {
            OverlapMinutes = customOverlapMinutes
        };

        _jwtOptionsMock
            .Setup(o => o.CurrentValue)
            .Returns(jwtOptions);

        var rotationId = Guid.NewGuid();
        var jwtKeyRotatedEvent = new JwtKeyRotatedEvent
        {
            RotationId = rotationId,
            RotatedAt = DateTimeOffset.UtcNow
        };

        _contextMock
            .Setup(c => c.Message)
            .Returns(jwtKeyRotatedEvent);

        _contextMock
            .Setup(c => c.CancellationToken)
            .Returns(CancellationToken.None);

        var currentKey = CreateMockSecurityKey();
        _signingKeyProviderMock
            .Setup(p => p.SigningKey)
            .Returns(currentKey);

        _vaultServiceMock
            .Setup(v => v.RefreshCredentials("haworks-identity", CancellationToken.None))
            .Returns(Task.CompletedTask);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        _dualKeyValidatorMock.Verify(v => v.SetPreviousKey(It.IsAny<SecurityKey>(), customOverlapMinutes), Times.Once);
    }

    private static SecurityKey CreateMockSecurityKey()
    {
        var mock = new Mock<SecurityKey>();
        mock.Setup(k => k.KeySize).Returns(256);
        return mock.Object;
    }
}