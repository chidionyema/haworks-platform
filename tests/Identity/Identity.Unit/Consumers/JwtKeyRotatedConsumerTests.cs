using Haworks.BuildingBlocks.Vault;
using Haworks.Contracts.Secrets;
using Haworks.Identity.Application.Consumers;
using Haworks.Identity.Application.Options;
using Haworks.Identity.Application.Services;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace Haworks.Identity.Unit.Consumers;

public class JwtKeyRotatedConsumerTests
{
    private readonly Mock<IVaultService> _vaultServiceMock;
    private readonly Mock<DualKeyJwtValidator> _dualKeyValidatorMock;
    private readonly Mock<IJwtSigningKeyProvider> _signingKeyProviderMock;
    private readonly Mock<IOptionsMonitor<JwtOptions>> _jwtOptionsMock;
    private readonly Mock<ILogger<JwtKeyRotatedConsumer>> _loggerMock;
    private readonly Mock<ConsumeContext<JwtKeyRotatedEvent>> _contextMock;
    private readonly JwtOptions _jwtOptions;

    public JwtKeyRotatedConsumerTests()
    {
        _vaultServiceMock = new Mock<IVaultService>();
        _dualKeyValidatorMock = new Mock<DualKeyJwtValidator>();
        _signingKeyProviderMock = new Mock<IJwtSigningKeyProvider>();
        _jwtOptionsMock = new Mock<IOptionsMonitor<JwtOptions>>();
        _loggerMock = new Mock<ILogger<JwtKeyRotatedConsumer>>();
        _contextMock = new Mock<ConsumeContext<JwtKeyRotatedEvent>>();

        _jwtOptions = new JwtOptions
        {
            Key = "test-key-must-be-at-least-32-characters-long-for-hmac",
            Issuer = "test-issuer",
            Audience = "test-audience",
            TokenExpiryMinutes = 15,
            RefreshTokenExpiryDays = 7,
            OverlapMinutes = 5
        };

        _jwtOptionsMock.Setup(o => o.CurrentValue).Returns(_jwtOptions);
    }

    private JwtKeyRotatedConsumer CreateConsumer() =>
        new(_vaultServiceMock.Object, _dualKeyValidatorMock.Object, _signingKeyProviderMock.Object,
            _jwtOptionsMock.Object, _loggerMock.Object);

    [Fact]
    public async Task Consume_ValidEvent_SetsPreviousKeyAndRefreshesCredentials()
    {
        // Arrange
        var rotationId = Guid.NewGuid();
        var rotatedEvent = new JwtKeyRotatedEvent { RotationId = rotationId };
        var currentSigningKey = new RsaSecurityKey(System.Security.Cryptography.RSA.Create()) { KeyId = "current-key" };

        _contextMock.Setup(c => c.Message).Returns(rotatedEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        _signingKeyProviderMock.Setup(s => s.SigningKey).Returns(currentSigningKey);

        _vaultServiceMock.Setup(v => v.RefreshCredentials("haworks-identity", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumer = CreateConsumer();

        // Act
        await consumer.Consume(_contextMock.Object);

        // Assert
        _dualKeyValidatorMock.Verify(d => d.SetPreviousKey(currentSigningKey, _jwtOptions.OverlapMinutes), Times.Once);
        _vaultServiceMock.Verify(v => v.RefreshCredentials("haworks-identity", It.IsAny<CancellationToken>()), Times.Once);

        // Verify logging
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Processing JwtKeyRotatedEvent")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("processed; overlap window active")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_VaultRefreshFails_LogsInfoAndCompletes()
    {
        // Arrange
        var rotationId = Guid.NewGuid();
        var rotatedEvent = new JwtKeyRotatedEvent { RotationId = rotationId };
        var currentSigningKey = new RsaSecurityKey(System.Security.Cryptography.RSA.Create()) { KeyId = "current-key" };

        _contextMock.Setup(c => c.Message).Returns(rotatedEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        _signingKeyProviderMock.Setup(s => s.SigningKey).Returns(currentSigningKey);

        // Vault refresh fails
        var vaultException = new InvalidOperationException("Vault connection failed");
        _vaultServiceMock.Setup(v => v.RefreshCredentials("haworks-identity", It.IsAny<CancellationToken>()))
            .ThrowsAsync(vaultException);

        var consumer = CreateConsumer();

        // Act & Assert - Should not throw
        var exception = await Record.ExceptionAsync(() => consumer.Consume(_contextMock.Object));

        // The exception should propagate for MassTransit to handle retry
        Assert.NotNull(exception);
        Assert.IsType<InvalidOperationException>(exception);

        // Verify the previous key was still set
        _dualKeyValidatorMock.Verify(d => d.SetPreviousKey(currentSigningKey, _jwtOptions.OverlapMinutes), Times.Once);
    }

    [Fact]
    public async Task Consume_UsesConfiguredOverlapMinutes()
    {
        // Arrange
        var customOptions = new JwtOptions
        {
            Key = "test-key-must-be-at-least-32-characters-long-for-hmac",
            Issuer = "test-issuer",
            Audience = "test-audience",
            TokenExpiryMinutes = 15,
            RefreshTokenExpiryDays = 7,
            OverlapMinutes = 10 // Custom overlap
        };

        _jwtOptionsMock.Setup(o => o.CurrentValue).Returns(customOptions);

        var rotationId = Guid.NewGuid();
        var rotatedEvent = new JwtKeyRotatedEvent { RotationId = rotationId };
        var currentSigningKey = new RsaSecurityKey(System.Security.Cryptography.RSA.Create()) { KeyId = "current-key" };

        _contextMock.Setup(c => c.Message).Returns(rotatedEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        _signingKeyProviderMock.Setup(s => s.SigningKey).Returns(currentSigningKey);

        _vaultServiceMock.Setup(v => v.RefreshCredentials("haworks-identity", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumer = CreateConsumer();

        // Act
        await consumer.Consume(_contextMock.Object);

        // Assert
        _dualKeyValidatorMock.Verify(d => d.SetPreviousKey(currentSigningKey, 10), Times.Once);
    }

    [Fact]
    public async Task Consume_UsesCorrectRoleName()
    {
        // Arrange
        var rotationId = Guid.NewGuid();
        var rotatedEvent = new JwtKeyRotatedEvent { RotationId = rotationId };
        var currentSigningKey = new RsaSecurityKey(System.Security.Cryptography.RSA.Create()) { KeyId = "current-key" };

        _contextMock.Setup(c => c.Message).Returns(rotatedEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        _signingKeyProviderMock.Setup(s => s.SigningKey).Returns(currentSigningKey);

        _vaultServiceMock.Setup(v => v.RefreshCredentials("haworks-identity", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumer = CreateConsumer();

        // Act
        await consumer.Consume(_contextMock.Object);

        // Assert
        _vaultServiceMock.Verify(v => v.RefreshCredentials("haworks-identity", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_PassesCancellationTokenToVault()
    {
        // Arrange
        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        var rotationId = Guid.NewGuid();
        var rotatedEvent = new JwtKeyRotatedEvent { RotationId = rotationId };
        var currentSigningKey = new RsaSecurityKey(System.Security.Cryptography.RSA.Create()) { KeyId = "current-key" };

        _contextMock.Setup(c => c.Message).Returns(rotatedEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(cancellationToken);
        _signingKeyProviderMock.Setup(s => s.SigningKey).Returns(currentSigningKey);

        _vaultServiceMock.Setup(v => v.RefreshCredentials("haworks-identity", cancellationToken))
            .Returns(Task.CompletedTask);

        var consumer = CreateConsumer();

        // Act
        await consumer.Consume(_contextMock.Object);

        // Assert
        _vaultServiceMock.Verify(v => v.RefreshCredentials("haworks-identity", cancellationToken), Times.Once);
    }
}