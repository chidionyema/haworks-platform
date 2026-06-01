using Haworks.Identity.Application;
using Haworks.Identity.Application.DTOs;
using Haworks.Identity.Application.Services;
using Haworks.BuildingBlocks.Common;
using Haworks.BuildingBlocks.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit.Abstractions;
using Xunit;

namespace Haworks.Identity.UnitTests.Commands;

public class CreateServiceTokenCommandHandlerTests : TestBase
{
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<CreateServiceTokenCommandHandler>> _loggerMock;
    private readonly CreateServiceTokenCommandHandler _handler;

    public CreateServiceTokenCommandHandlerTests(ITestOutputHelper output) : base(output)
    {
        _jwtTokenServiceMock = new Mock<IJwtTokenService>();
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<CreateServiceTokenCommandHandler>>();

        _handler = new CreateServiceTokenCommandHandler(
            _jwtTokenServiceMock.Object,
            _configurationMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidServiceSecret_ReturnsServiceToken()
    {
        // Arrange
        var validSecret = "valid-service-secret";
        var command = new CreateServiceTokenCommand(validSecret);
        var expectedToken = "service-jwt-token";
        var expectedExpires = DateTime.UtcNow.AddHours(1);

        _configurationMock.Setup(c => c["SERVICE_SECRET"])
            .Returns(validSecret);
        _jwtTokenServiceMock.Setup(j => j.GenerateServiceToken())
            .Returns(expectedToken);
        _jwtTokenServiceMock.Setup(j => j.GetServiceTokenExpiration())
            .Returns(expectedExpires);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var serviceTokenDto = result.Value;
        Assert.Equal(expectedToken, serviceTokenDto.Token);
        Assert.Equal(expectedExpires, serviceTokenDto.Expires);
        Assert.Equal("system", serviceTokenDto.ServiceName);
    }

    [Fact]
    public async Task Handle_WithInvalidServiceSecret_ReturnsInvalidServiceSecretError()
    {
        // Arrange
        var invalidSecret = "wrong-secret";
        var validSecret = "correct-service-secret";
        var command = new CreateServiceTokenCommand(invalidSecret);

        _configurationMock.Setup(c => c["SERVICE_SECRET"])
            .Returns(validSecret);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(Error.Authentication("Auth.InvalidServiceSecret", "Invalid service secret"), result.Error);
    }

    [Fact]
    public async Task Handle_WithNullServiceSecret_ReturnsInvalidServiceSecretError()
    {
        // Arrange
        var command = new CreateServiceTokenCommand(null!);
        var configuredSecret = "valid-secret";

        _configurationMock.Setup(c => c["SERVICE_SECRET"])
            .Returns(configuredSecret);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(Error.Authentication("Auth.InvalidServiceSecret", "Invalid service secret"), result.Error);
    }

    [Fact]
    public async Task Handle_WithEmptyServiceSecret_ReturnsInvalidServiceSecretError()
    {
        // Arrange
        var command = new CreateServiceTokenCommand("");
        var configuredSecret = "valid-secret";

        _configurationMock.Setup(c => c["SERVICE_SECRET"])
            .Returns(configuredSecret);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(Error.Authentication("Auth.InvalidServiceSecret", "Invalid service secret"), result.Error);
    }

    [Fact]
    public async Task Handle_WithMissingConfiguredSecret_ReturnsInvalidServiceSecretError()
    {
        // Arrange
        var command = new CreateServiceTokenCommand("any-secret");

        _configurationMock.Setup(c => c["SERVICE_SECRET"])
            .Returns((string?)null); // No configured secret

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(Error.Authentication("Auth.InvalidServiceSecret", "Invalid service secret"), result.Error);
    }

    [Fact]
    public async Task Handle_WithWhitespaceSecret_ReturnsInvalidServiceSecretError()
    {
        // Arrange
        var command = new CreateServiceTokenCommand("   ");
        var configuredSecret = "valid-secret";

        _configurationMock.Setup(c => c["SERVICE_SECRET"])
            .Returns(configuredSecret);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(Error.Authentication("Auth.InvalidServiceSecret", "Invalid service secret"), result.Error);
    }

    [Fact]
    public async Task Handle_IsCaseSensitive_WithDifferentCasing()
    {
        // Arrange
        var configuredSecret = "CaseSensitiveSecret";
        var providedSecret = "casesensitivesecret"; // Different casing
        var command = new CreateServiceTokenCommand(providedSecret);

        _configurationMock.Setup(c => c["SERVICE_SECRET"])
            .Returns(configuredSecret);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(Error.Authentication("Auth.InvalidServiceSecret", "Invalid service secret"), result.Error);
    }

    [Fact]
    public async Task Handle_LogsSecurityEvent_OnInvalidSecret()
    {
        // Arrange
        var invalidSecret = "hacker-secret";
        var validSecret = "correct-secret";
        var command = new CreateServiceTokenCommand(invalidSecret);

        _configurationMock.Setup(c => c["SERVICE_SECRET"])
            .Returns(validSecret);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        VerifyLogMessage(LogLevel.Warning, "Invalid service secret provided");
    }

    [Fact]
    public async Task Handle_LogsSuccessEvent_OnValidSecret()
    {
        // Arrange
        var validSecret = "valid-service-secret";
        var command = new CreateServiceTokenCommand(validSecret);
        var expectedToken = "service-jwt-token";
        var expectedExpires = DateTime.UtcNow.AddHours(1);

        _configurationMock.Setup(c => c["SERVICE_SECRET"])
            .Returns(validSecret);
        _jwtTokenServiceMock.Setup(j => j.GenerateServiceToken())
            .Returns(expectedToken);
        _jwtTokenServiceMock.Setup(j => j.GetServiceTokenExpiration())
            .Returns(expectedExpires);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        VerifyLogMessage(LogLevel.Information, "Service token generated successfully");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_WithInvalidConfiguredSecret_ReturnsError(string? configuredSecret)
    {
        // Arrange
        var command = new CreateServiceTokenCommand("any-secret");

        _configurationMock.Setup(c => c["SERVICE_SECRET"])
            .Returns(configuredSecret);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(Error.Authentication("Auth.InvalidServiceSecret", "Invalid service secret"), result.Error);
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