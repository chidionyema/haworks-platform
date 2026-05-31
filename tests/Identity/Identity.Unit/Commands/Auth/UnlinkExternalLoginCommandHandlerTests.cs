using Haworks.Identity.Application;
using Haworks.Identity.Domain;
using Haworks.BuildingBlocks.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Haworks.Identity.Unit.Commands.Auth;

public class UnlinkExternalLoginCommandHandlerTests
{
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<ILogger<UnlinkExternalLoginCommandHandler>> _loggerMock;

    public UnlinkExternalLoginCommandHandlerTests()
    {
        var userStoreMock = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _loggerMock = new Mock<ILogger<UnlinkExternalLoginCommandHandler>>();
    }

    private UnlinkExternalLoginCommandHandler CreateHandler() =>
        new(_userManagerMock.Object, _loggerMock.Object);

    [Fact]
    public async Task Handle_EmptyUserId_ReturnsMissingUserIdError()
    {
        // Arrange
        var command = new UnlinkExternalLoginCommand("", "Google");
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(Error.Auth.MissingUserId, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Handle_InvalidUserId_ReturnsMissingUserIdError(string? invalidUserId)
    {
        // Arrange
        var command = new UnlinkExternalLoginCommand(invalidUserId!, "Google");
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
        var command = new UnlinkExternalLoginCommand(userId, "Google");
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
    public async Task Handle_LoginNotFound_ReturnsLoginNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var command = new UnlinkExternalLoginCommand(userId, "Google");
        var handler = CreateHandler();

        // User has no external logins
        var existingLogins = new List<UserLoginInfo>();

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.GetLoginsAsync(user))
            .ReturnsAsync(existingLogins);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(Error.Auth.LoginNotFound, result.Error);
    }

    [Fact]
    public async Task Handle_DifferentProviderLogin_ReturnsLoginNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var command = new UnlinkExternalLoginCommand(userId, "Google");
        var handler = CreateHandler();

        // User has Microsoft login but not Google
        var existingLogin = new UserLoginInfo("Microsoft", "ms123", "Microsoft");
        var existingLogins = new List<UserLoginInfo> { existingLogin };

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.GetLoginsAsync(user))
            .ReturnsAsync(existingLogins);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(Error.Auth.LoginNotFound, result.Error);
    }

    [Fact]
    public async Task Handle_SuccessfulUnlink_ReturnsSuccess()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var command = new UnlinkExternalLoginCommand(userId, "Google");
        var handler = CreateHandler();

        var googleLogin = new UserLoginInfo("Google", "google123", "Google");
        var existingLogins = new List<UserLoginInfo> { googleLogin };

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.GetLoginsAsync(user))
            .ReturnsAsync(existingLogins);
        _userManagerMock.Setup(u => u.RemoveLoginAsync(user, "Google", "google123"))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);

        // Verify RemoveLoginAsync was called with correct parameters
        _userManagerMock.Verify(u => u.RemoveLoginAsync(user, "Google", "google123"), Times.Once);

        // Verify success logging
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Removed Google login from user")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_RemoveLoginFails_ReturnsUnlinkFailedError()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var command = new UnlinkExternalLoginCommand(userId, "Google");
        var handler = CreateHandler();

        var googleLogin = new UserLoginInfo("Google", "google123", "Google");
        var existingLogins = new List<UserLoginInfo> { googleLogin };
        var identityError = new IdentityError { Code = "UnlinkFailed", Description = "Failed to remove external login" };

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.GetLoginsAsync(user))
            .ReturnsAsync(existingLogins);
        _userManagerMock.Setup(u => u.RemoveLoginAsync(user, "Google", "google123"))
            .ReturnsAsync(IdentityResult.Failed(identityError));

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(Error.Auth.UnlinkFailed, result.Error);

        // Verify error logging
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to unlink external login")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_MultipleLogins_UnlinksCorrectProvider()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var command = new UnlinkExternalLoginCommand(userId, "Google");
        var handler = CreateHandler();

        var googleLogin = new UserLoginInfo("Google", "google123", "Google");
        var microsoftLogin = new UserLoginInfo("Microsoft", "ms456", "Microsoft");
        var facebookLogin = new UserLoginInfo("Facebook", "fb789", "Facebook");
        var existingLogins = new List<UserLoginInfo> { googleLogin, microsoftLogin, facebookLogin };

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.GetLoginsAsync(user))
            .ReturnsAsync(existingLogins);
        _userManagerMock.Setup(u => u.RemoveLoginAsync(user, "Google", "google123"))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);

        // Verify only Google login was removed
        _userManagerMock.Verify(u => u.RemoveLoginAsync(user, "Google", "google123"), Times.Once);
        _userManagerMock.Verify(u => u.RemoveLoginAsync(user, "Microsoft", It.IsAny<string>()), Times.Never);
        _userManagerMock.Verify(u => u.RemoveLoginAsync(user, "Facebook", It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CaseInsensitiveProviderMatching_FindsLogin()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var command = new UnlinkExternalLoginCommand(userId, "google"); // lowercase
        var handler = CreateHandler();

        var googleLogin = new UserLoginInfo("Google", "google123", "Google"); // Pascal case
        var existingLogins = new List<UserLoginInfo> { googleLogin };

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.GetLoginsAsync(user))
            .ReturnsAsync(existingLogins);
        _userManagerMock.Setup(u => u.RemoveLoginAsync(user, "Google", "google123"))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);

        // Verify the login was found despite case difference
        _userManagerMock.Verify(u => u.RemoveLoginAsync(user, "Google", "google123"), Times.Once);
    }

    [Fact]
    public async Task Handle_VerifiesLoginProviderComparison()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var command = new UnlinkExternalLoginCommand(userId, "Google");
        var handler = CreateHandler();

        // Create logins with similar names to ensure exact matching
        var googleLogin = new UserLoginInfo("Google", "google123", "Google");
        var googleplusLogin = new UserLoginInfo("GooglePlus", "gplus123", "Google+");
        var existingLogins = new List<UserLoginInfo> { googleLogin, googleplusLogin };

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.GetLoginsAsync(user))
            .ReturnsAsync(existingLogins);
        _userManagerMock.Setup(u => u.RemoveLoginAsync(user, "Google", "google123"))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);

        // Verify only exact "Google" match was removed, not "GooglePlus"
        _userManagerMock.Verify(u => u.RemoveLoginAsync(user, "Google", "google123"), Times.Once);
        _userManagerMock.Verify(u => u.RemoveLoginAsync(user, "GooglePlus", It.IsAny<string>()), Times.Never);
    }
}