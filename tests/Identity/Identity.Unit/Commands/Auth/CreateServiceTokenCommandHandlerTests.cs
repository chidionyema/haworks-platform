using Haworks.Identity.Application.Commands.Auth;
using Haworks.Identity.Application.Interfaces;
using Haworks.BuildingBlocks.Testing;
using Moq;
using Xunit.Abstractions;
using Xunit;

namespace Haworks.Identity.UnitTests.Commands.Auth;

public class CreateServiceTokenCommandHandlerTests : TestBase
{
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock;
    private readonly CreateServiceTokenCommandHandler _handler;

    public CreateServiceTokenCommandHandlerTests(ITestOutputHelper output) : base(output)
    {
        _jwtTokenServiceMock = new Mock<IJwtTokenService>();
        _handler = new CreateServiceTokenCommandHandler(_jwtTokenServiceMock.Object);
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessResultWithToken()
    {
        // Arrange
        var command = new CreateServiceTokenCommand("test-idempotency-key");
        var expectedToken = "service-jwt-token";

        _jwtTokenServiceMock
            .Setup(j => j.GenerateServiceTokenAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(expectedToken);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedToken, result.Value);
    }

    [Fact]
    public async Task Handle_ValidCommand_CallsJwtServiceWithCorrectExpiry()
    {
        // Arrange
        var command = new CreateServiceTokenCommand("test-idempotency-key");
        var beforeCall = DateTime.UtcNow;

        _jwtTokenServiceMock
            .Setup(j => j.GenerateServiceTokenAsync(It.IsAny<DateTime>()))
            .ReturnsAsync("token");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _jwtTokenServiceMock.Verify(j => j.GenerateServiceTokenAsync(
            It.Is<DateTime>(expiry =>
                expiry > beforeCall.AddMinutes(25) && // Allow 5 minute tolerance
                expiry < beforeCall.AddMinutes(35))), // 30 minute expiry +/- tolerance
            Times.Once);
    }

    [Fact]
    public async Task Handle_EmptyIdempotencyKey_StillSucceeds()
    {
        // Arrange
        var command = new CreateServiceTokenCommand("");
        var expectedToken = "service-jwt-token";

        _jwtTokenServiceMock
            .Setup(j => j.GenerateServiceTokenAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(expectedToken);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedToken, result.Value);
    }

    [Fact]
    public async Task Handle_JwtServiceThrowsException_ThrowsException()
    {
        // Arrange
        var command = new CreateServiceTokenCommand("test-idempotency-key");

        _jwtTokenServiceMock
            .Setup(j => j.GenerateServiceTokenAsync(It.IsAny<DateTime>()))
            .ThrowsAsync(new InvalidOperationException("JWT service error"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_DefaultIdempotencyKey_UsesEmptyString()
    {
        // Arrange
        var command = new CreateServiceTokenCommand(); // Default value
        var expectedToken = "service-jwt-token";

        _jwtTokenServiceMock
            .Setup(j => j.GenerateServiceTokenAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(expectedToken);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedToken, result.Value);
        Assert.Equal("", command.IdempotencyKey);
    }

    [Theory]
    [InlineData("key1")]
    [InlineData("key2")]
    [InlineData("different-key")]
    public async Task Handle_DifferentIdempotencyKeys_AllSucceed(string idempotencyKey)
    {
        // Arrange
        var command = new CreateServiceTokenCommand(idempotencyKey);
        var expectedToken = $"token-for-{idempotencyKey}";

        _jwtTokenServiceMock
            .Setup(j => j.GenerateServiceTokenAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(expectedToken);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedToken, result.Value);
    }
}