using Haworks.Identity.Application;
using Haworks.Identity.Domain;
using Haworks.BuildingBlocks.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Haworks.Identity.Unit.Queries;

public class GetUserLoginsQueryHandlerTests
{
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<ILogger<GetUserLoginsQueryHandler>> _loggerMock;

    public GetUserLoginsQueryHandlerTests()
    {
        var userStoreMock = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _loggerMock = new Mock<ILogger<GetUserLoginsQueryHandler>>();
    }

    private GetUserLoginsQueryHandler CreateHandler() =>
        new(_userManagerMock.Object, _loggerMock.Object);

    [Fact]
    public async Task Handle_EmptyUserId_ReturnsMissingUserIdError()
    {
        // Arrange
        var query = new GetUserLoginsQuery("");
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Auth.MissingUserId", result.Error.Code);
        Assert.Equal("User identifier not found", result.Error.Message);

        // Verify warning was logged
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("User identifier not found in GetUserLogins")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Handle_InvalidUserId_ReturnsMissingUserIdError(string? invalidUserId)
    {
        // Arrange
        var query = new GetUserLoginsQuery(invalidUserId!);
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Auth.MissingUserId", result.Error.Code);
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsUserNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var query = new GetUserLoginsQuery(userId);
        var handler = CreateHandler();

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync((User)null!);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Auth.UserNotFound", result.Error.Code);
        Assert.Equal("User not found", result.Error.Message);

        // Verify warning was logged
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("User not found in GetUserLogins")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UserWithNoLogins_ReturnsEmptyList()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var query = new GetUserLoginsQuery(userId);
        var handler = CreateHandler();

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.GetLoginsAsync(user))
            .ReturnsAsync(new List<UserLoginInfo>());

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);

        // Verify logging
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("GetUserLogins called for user ID")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Found 0 logins for user")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UserWithSingleLogin_ReturnsLoginInfo()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var query = new GetUserLoginsQuery(userId);
        var handler = CreateHandler();

        var googleLogin = new UserLoginInfo("Google", "google123", "Google");
        var logins = new List<UserLoginInfo> { googleLogin };

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.GetLoginsAsync(user))
            .ReturnsAsync(logins);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);

        var loginDto = result.Value.First();
        Assert.Equal("Google", loginDto.Provider);
        Assert.Equal("Google", loginDto.ProviderDisplayName);

        // Verify logging
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Found 1 logins for user")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UserWithMultipleLogins_ReturnsAllLogins()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var query = new GetUserLoginsQuery(userId);
        var handler = CreateHandler();

        var googleLogin = new UserLoginInfo("Google", "google123", "Google");
        var microsoftLogin = new UserLoginInfo("Microsoft", "ms456", "Microsoft");
        var facebookLogin = new UserLoginInfo("Facebook", "fb789", "Facebook");
        var logins = new List<UserLoginInfo> { googleLogin, microsoftLogin, facebookLogin };

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.GetLoginsAsync(user))
            .ReturnsAsync(logins);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);

        var providers = result.Value.Select(l => l.Provider).ToList();
        Assert.Contains("Google", providers);
        Assert.Contains("Microsoft", providers);
        Assert.Contains("Facebook", providers);

        var displayNames = result.Value.Select(l => l.ProviderDisplayName).ToList();
        Assert.Contains("Google", displayNames);
        Assert.Contains("Microsoft", displayNames);
        Assert.Contains("Facebook", displayNames);

        // Verify logging shows correct count
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Found 3 logins for user")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_LoginWithNullDisplayName_MapsCorrectly()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var query = new GetUserLoginsQuery(userId);
        var handler = CreateHandler();

        var customLogin = new UserLoginInfo("CustomProvider", "custom123", null); // null display name
        var logins = new List<UserLoginInfo> { customLogin };

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.GetLoginsAsync(user))
            .ReturnsAsync(logins);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);

        var loginDto = result.Value.First();
        Assert.Equal("CustomProvider", loginDto.Provider);
        Assert.Null(loginDto.ProviderDisplayName);
    }

    [Fact]
    public async Task Handle_LogsCorrectUserIdInMessages()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var query = new GetUserLoginsQuery(userId);
        var handler = CreateHandler();

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.GetLoginsAsync(user))
            .ReturnsAsync(new List<UserLoginInfo>());

        // Act
        await handler.Handle(query, CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"GetUserLogins called for user ID: {userId}")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Found 0 logins for user {userId}")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PreservesLoginOrder()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var query = new GetUserLoginsQuery(userId);
        var handler = CreateHandler();

        // Create logins in specific order
        var login1 = new UserLoginInfo("Provider1", "key1", "Display1");
        var login2 = new UserLoginInfo("Provider2", "key2", "Display2");
        var login3 = new UserLoginInfo("Provider3", "key3", "Display3");
        var logins = new List<UserLoginInfo> { login1, login2, login3 };

        _userManagerMock.Setup(u => u.FindByIdAsync(userId))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.GetLoginsAsync(user))
            .ReturnsAsync(logins);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);

        // Verify order is preserved
        Assert.Equal("Provider1", result.Value[0].Provider);
        Assert.Equal("Provider2", result.Value[1].Provider);
        Assert.Equal("Provider3", result.Value[2].Provider);
    }
}