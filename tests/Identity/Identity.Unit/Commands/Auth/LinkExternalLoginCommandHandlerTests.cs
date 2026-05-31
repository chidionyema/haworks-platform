using System.Security.Claims;
using Haworks.Identity.Application;
using Haworks.Identity.Domain;
using Haworks.BuildingBlocks.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Haworks.Identity.Unit.Commands.Auth;

public class LinkExternalLoginCommandHandlerTests
{
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<ILogger<LinkExternalLoginCommandHandler>> _loggerMock;

    public LinkExternalLoginCommandHandlerTests()
    {
        var userStoreMock = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _loggerMock = new Mock<ILogger<LinkExternalLoginCommandHandler>>();
    }

    private LinkExternalLoginCommandHandler CreateHandler() =>
        new(_userManagerMock.Object, _loggerMock.Object);

    [Fact]
    public async Task Handle_EmptyUserId_ReturnsMissingUserIdError()
    {
        // Arrange
        var command = new LinkExternalLoginCommand("", "Google", null);
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(Error.Auth.MissingUserId, result.Error);
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsUserNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var command = new LinkExternalLoginCommand(userId, "Google", null);
        var handler = CreateHandler();

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync((User)null!);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(Error.Auth.UserNotFound, result.Error);
    }

    [Fact]
    public async Task Handle_NoLoginInfo_ReturnsRequiresChallenge()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var command = new LinkExternalLoginCommand(userId, "Google", null);
        var handler = CreateHandler();

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.RequiresChallenge);
        Assert.Null(result.Value.Message);
    }

    [Fact]
    public async Task Handle_LoginInfoWithEmptyProviderKey_ReturnsInvalidProviderKeyError()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var claims = new[] { new Claim(ClaimTypes.Email, "test@example.com") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var loginInfo = new ExternalLoginInfo(principal, "Google", "", "Google");
        var command = new LinkExternalLoginCommand(userId, "Google", loginInfo);
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(Error.Auth.InvalidProviderKey, result.Error);
    }

    [Fact]
    public async Task Handle_LoginInfoWithUserNotFound_ReturnsUserNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var claims = new[] { new Claim(ClaimTypes.Email, "test@example.com") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var loginInfo = new ExternalLoginInfo(principal, "Google", "123456", "Google");
        var command = new LinkExternalLoginCommand(userId, "Google", loginInfo);
        var handler = CreateHandler();

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync((User)null!);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(Error.Auth.UserNotFound, result.Error);
    }

    [Fact]
    public async Task Handle_UserAlreadyHasProviderLogin_ReturnsAlreadyLinkedError()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var claims = new[] { new Claim(ClaimTypes.Email, "test@example.com") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var loginInfo = new ExternalLoginInfo(principal, "Google", "123456", "Google");
        var command = new LinkExternalLoginCommand(userId, "Google", loginInfo);
        var handler = CreateHandler();

        var existingLogin = new UserLoginInfo("Google", "789", "Google");
        var existingLogins = new List<UserLoginInfo> { existingLogin };

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.GetLoginsAsync(user))
            .ReturnsAsync(existingLogins);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Auth.AlreadyLinked", result.Error.Code);
    }

    [Fact]
    public async Task Handle_SuccessfulLink_ReturnsSuccessWithMessage()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var claims = new[] { new Claim(ClaimTypes.Email, "test@example.com") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var loginInfo = new ExternalLoginInfo(principal, "Google", "123456", "Google");
        var command = new LinkExternalLoginCommand(userId, "Google", loginInfo);
        var handler = CreateHandler();

        var existingLogins = new List<UserLoginInfo>(); // No existing Google login

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.GetLoginsAsync(user))
            .ReturnsAsync(existingLogins);
        _userManagerMock.Setup(u => u.AddLoginAsync(user, loginInfo))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.RequiresChallenge);
        Assert.Equal("Successfully linked Google login", result.Value.Message);

        // Verify success audit log
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("External login Google successfully linked")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_LinkFails_ReturnsLinkFailedError()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var claims = new[] { new Claim(ClaimTypes.Email, "test@example.com") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var loginInfo = new ExternalLoginInfo(principal, "Google", "123456", "Google");
        var command = new LinkExternalLoginCommand(userId, "Google", loginInfo);
        var handler = CreateHandler();

        var existingLogins = new List<UserLoginInfo>(); // No existing Google login
        var identityError = new IdentityError { Code = "LinkFailed", Description = "Failed to link external login" };

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.GetLoginsAsync(user))
            .ReturnsAsync(existingLogins);
        _userManagerMock.Setup(u => u.AddLoginAsync(user, loginInfo))
            .ReturnsAsync(IdentityResult.Failed(identityError));

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(Error.Auth.LinkFailed, result.Error);

        // Verify error logging without exposing details
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to link external login")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UserHasDifferentProviderLogin_SuccessfullyLinksNewProvider()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var claims = new[] { new Claim(ClaimTypes.Email, "test@example.com") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var loginInfo = new ExternalLoginInfo(principal, "Google", "123456", "Google");
        var command = new LinkExternalLoginCommand(userId, "Google", loginInfo);
        var handler = CreateHandler();

        // User has Microsoft login but not Google
        var existingLogin = new UserLoginInfo("Microsoft", "ms123", "Microsoft");
        var existingLogins = new List<UserLoginInfo> { existingLogin };

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.GetLoginsAsync(user))
            .ReturnsAsync(existingLogins);
        _userManagerMock.Setup(u => u.AddLoginAsync(user, loginInfo))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.RequiresChallenge);
        Assert.Equal("Successfully linked Google login", result.Value.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_InvalidUserId_ReturnsMissingUserIdError(string? invalidUserId)
    {
        // Arrange
        var command = new LinkExternalLoginCommand(invalidUserId!, "Google", null);
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(Error.Auth.MissingUserId, result.Error);
    }

    [Fact]
    public async Task Handle_ValidatesProviderNameConsistency()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var claims = new[] { new Claim(ClaimTypes.Email, "test@example.com") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        // LoginInfo has different provider than command
        var loginInfo = new ExternalLoginInfo(principal, "Microsoft", "123456", "Microsoft");
        var command = new LinkExternalLoginCommand(userId, "Google", loginInfo);
        var handler = CreateHandler();

        var existingLogins = new List<UserLoginInfo>();

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.GetLoginsAsync(user))
            .ReturnsAsync(existingLogins);
        _userManagerMock.Setup(u => u.AddLoginAsync(user, loginInfo))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.RequiresChallenge);
        // Should use the command provider name, not loginInfo provider
        Assert.Equal("Successfully linked Google login", result.Value.Message);
    }
}