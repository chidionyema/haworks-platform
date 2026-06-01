using Haworks.Identity.Application;
using Haworks.Identity.Application.DTOs;
using Haworks.Identity.Application.Services;
using Haworks.Identity.Domain;
using Haworks.BuildingBlocks.Common;
using Haworks.BuildingBlocks.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;
using Xunit.Abstractions;
using Xunit;

namespace Haworks.Identity.UnitTests.Commands;

public class ExternalLoginCallbackCommandHandlerTests : TestBase
{
    private readonly Mock<SignInManager<User>> _signInManagerMock;
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock;
    private readonly Mock<IRefreshTokenService> _refreshTokenServiceMock;
    private readonly Mock<ILogger<ExternalLoginCallbackCommandHandler>> _loggerMock;
    private readonly Mock<IOptions<ExternalLoginOptions>> _optionsMock;
    private readonly ExternalLoginCallbackCommandHandler _handler;
    private readonly ExternalLoginOptions _options;

    public ExternalLoginCallbackCommandHandlerTests(ITestOutputHelper output) : base(output)
    {
        var userStoreMock = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(
            userStoreMock.Object,
            null, null, null, null, null, null, null, null);

        _signInManagerMock = new Mock<SignInManager<User>>(
            _userManagerMock.Object,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<User>>(),
            null, null, null, null);

        _jwtTokenServiceMock = new Mock<IJwtTokenService>();
        _refreshTokenServiceMock = new Mock<IRefreshTokenService>();
        _loggerMock = new Mock<ILogger<ExternalLoginCallbackCommandHandler>>();

        _options = new ExternalLoginOptions
        {
            TrustedEmailProviders = new[] { "Google", "Microsoft", "Apple" },
            MaxUserNameLength = 50
        };
        _optionsMock = new Mock<IOptions<ExternalLoginOptions>>();
        _optionsMock.Setup(x => x.Value).Returns(_options);

        _handler = new ExternalLoginCallbackCommandHandler(
            _signInManagerMock.Object,
            _userManagerMock.Object,
            _jwtTokenServiceMock.Object,
            _refreshTokenServiceMock.Object,
            _loggerMock.Object,
            _optionsMock.Object);
    }

    [Fact]
    public async Task Handle_WithNullHttpContext_ReturnsInvalidContextError()
    {
        // Arrange
        var command = new ExternalLoginCallbackCommand(null!);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(Error.Auth.InvalidContext, result.Error);
    }

    [Fact]
    public async Task Handle_WithNoExternalLoginInfo_ReturnsExternalLoginFailedError()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext);

        _signInManagerMock.Setup(s => s.GetExternalLoginInfoAsync())
            .ReturnsAsync((ExternalLoginInfo?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(Error.Auth.ExternalLoginFailed, result.Error);
    }

    [Fact]
    public async Task Handle_WithEmptyProviderKey_ReturnsInvalidProviderKeyError()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext);

        var loginInfo = new ExternalLoginInfo(
            new ClaimsPrincipal(),
            "Google",
            "", // Empty provider key
            "Google");

        _signInManagerMock.Setup(s => s.GetExternalLoginInfoAsync())
            .ReturnsAsync(loginInfo);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(Error.Auth.InvalidProviderKey, result.Error);
    }

    [Fact]
    public async Task Handle_WithSuccessfulExternalSignIn_ReturnsAuthResponse()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext);

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "testuser",
            Email = "test@example.com"
        };

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "google-123"),
            new Claim(ClaimTypes.Email, "test@example.com")
        };

        var loginInfo = new ExternalLoginInfo(
            new ClaimsPrincipal(new ClaimsIdentity(claims)),
            "Google",
            "google-123",
            "Google");

        var expectedToken = "jwt-token";
        var expectedRefreshToken = "refresh-token";
        var expectedExpires = DateTime.UtcNow.AddHours(1);

        _signInManagerMock.Setup(s => s.GetExternalLoginInfoAsync())
            .ReturnsAsync(loginInfo);
        _signInManagerMock.Setup(s => s.ExternalLoginSignInAsync("Google", "google-123", false, false))
            .ReturnsAsync(SignInResult.Success);
        _userManagerMock.Setup(u => u.FindByLoginAsync("Google", "google-123"))
            .ReturnsAsync(user);

        _jwtTokenServiceMock.Setup(j => j.GenerateToken(user, It.IsAny<IList<string>>()))
            .Returns(expectedToken);
        _jwtTokenServiceMock.Setup(j => j.GetTokenExpiration())
            .Returns(expectedExpires);
        _refreshTokenServiceMock.Setup(r => r.GenerateRefreshTokenAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedRefreshToken);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var authResponse = result.Value;
        Assert.Equal(expectedToken, authResponse.Token);
        Assert.Equal(expectedRefreshToken, authResponse.RefreshToken);
        Assert.Equal(expectedExpires, authResponse.Expires);
        Assert.Equal(user.UserName, authResponse.Username);
        Assert.Equal(user.Email, authResponse.Email);
        Assert.Equal(user.Id, authResponse.UserId);
    }

    [Fact]
    public async Task Handle_WithFailedExternalSignInAndNewUser_CreatesUserAndReturnsAuthResponse()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "google-456"),
            new Claim(ClaimTypes.Email, "newuser@example.com"),
            new Claim(ClaimTypes.Name, "New User"),
            new Claim("email_verified", "true")
        };

        var loginInfo = new ExternalLoginInfo(
            new ClaimsPrincipal(new ClaimsIdentity(claims)),
            "Google",
            "google-456",
            "Google");

        var expectedToken = "jwt-token";
        var expectedRefreshToken = "refresh-token";
        var expectedExpires = DateTime.UtcNow.AddHours(1);

        _signInManagerMock.Setup(s => s.GetExternalLoginInfoAsync())
            .ReturnsAsync(loginInfo);
        _signInManagerMock.Setup(s => s.ExternalLoginSignInAsync("Google", "google-456", false, false))
            .ReturnsAsync(SignInResult.Failed);

        _userManagerMock.Setup(u => u.FindByEmailAsync("newuser@example.com"))
            .ReturnsAsync((User?)null);
        _userManagerMock.Setup(u => u.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(u => u.AddLoginAsync(It.IsAny<User>(), loginInfo))
            .ReturnsAsync(IdentityResult.Success);

        _jwtTokenServiceMock.Setup(j => j.GenerateToken(It.IsAny<User>(), It.IsAny<IList<string>>()))
            .Returns(expectedToken);
        _jwtTokenServiceMock.Setup(j => j.GetTokenExpiration())
            .Returns(expectedExpires);
        _refreshTokenServiceMock.Setup(r => r.GenerateRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedRefreshToken);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _userManagerMock.Verify(u => u.CreateAsync(It.Is<User>(user =>
            user.Email == "newuser@example.com" &&
            user.UserName.StartsWith("newuser") &&
            user.EmailConfirmed == true)), Times.Once);
        _userManagerMock.Verify(u => u.AddLoginAsync(It.IsAny<User>(), loginInfo), Times.Once);

        var authResponse = result.Value;
        Assert.Equal(expectedToken, authResponse.Token);
        Assert.Equal(expectedRefreshToken, authResponse.RefreshToken);
    }

    [Fact]
    public async Task Handle_WithEmailFromUntrustedProvider_RequiresEmailVerification()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "facebook-789"),
            new Claim(ClaimTypes.Email, "unverified@example.com"),
            new Claim(ClaimTypes.Name, "Unverified User")
            // Note: no email_verified claim for Facebook
        };

        var loginInfo = new ExternalLoginInfo(
            new ClaimsPrincipal(new ClaimsIdentity(claims)),
            "Facebook", // Not in trusted providers
            "facebook-789",
            "Facebook");

        _signInManagerMock.Setup(s => s.GetExternalLoginInfoAsync())
            .ReturnsAsync(loginInfo);
        _signInManagerMock.Setup(s => s.ExternalLoginSignInAsync("Facebook", "facebook-789", false, false))
            .ReturnsAsync(SignInResult.Failed);

        _userManagerMock.Setup(u => u.FindByEmailAsync("unverified@example.com"))
            .ReturnsAsync((User?)null);
        _userManagerMock.Setup(u => u.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(u => u.AddLoginAsync(It.IsAny<User>(), loginInfo))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _userManagerMock.Verify(u => u.CreateAsync(It.Is<User>(user =>
            user.EmailConfirmed == false)), Times.Once);
    }

    [Fact]
    public async Task Handle_WithUserCreationFailure_ReturnsFailure()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "google-error"),
            new Claim(ClaimTypes.Email, "error@example.com")
        };

        var loginInfo = new ExternalLoginInfo(
            new ClaimsPrincipal(new ClaimsIdentity(claims)),
            "Google",
            "google-error",
            "Google");

        _signInManagerMock.Setup(s => s.GetExternalLoginInfoAsync())
            .ReturnsAsync(loginInfo);
        _signInManagerMock.Setup(s => s.ExternalLoginSignInAsync("Google", "google-error", false, false))
            .ReturnsAsync(SignInResult.Failed);

        _userManagerMock.Setup(u => u.FindByEmailAsync("error@example.com"))
            .ReturnsAsync((User?)null);

        var identityError = new IdentityError { Description = "Email already exists" };
        _userManagerMock.Setup(u => u.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Failed(identityError));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Email already exists", result.Error.Message);
    }

    [Theory]
    [InlineData("test.user@example.com", "testuser")] // Email-based username
    [InlineData("user with spaces", "userwithspaces")] // Space removal
    [InlineData("user@with@special@chars", "userwithspecialchars")] // Special char removal
    [InlineData("very-long-username-that-exceeds-the-maximum-allowed-length-for-usernames", "very-long-username-that-exceeds-the-maximum-allow")] // Truncation
    public async Task Handle_GeneratesValidUsername_FromEmail(string email, string expectedUsernamePrefix)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "google-username-test"),
            new Claim(ClaimTypes.Email, email)
        };

        var loginInfo = new ExternalLoginInfo(
            new ClaimsPrincipal(new ClaimsIdentity(claims)),
            "Google",
            "google-username-test",
            "Google");

        _signInManagerMock.Setup(s => s.GetExternalLoginInfoAsync())
            .ReturnsAsync(loginInfo);
        _signInManagerMock.Setup(s => s.ExternalLoginSignInAsync("Google", "google-username-test", false, false))
            .ReturnsAsync(SignInResult.Failed);

        _userManagerMock.Setup(u => u.FindByEmailAsync(email))
            .ReturnsAsync((User?)null);
        _userManagerMock.Setup(u => u.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(u => u.AddLoginAsync(It.IsAny<User>(), loginInfo))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _userManagerMock.Verify(u => u.CreateAsync(It.Is<User>(user =>
            user.UserName.StartsWith(expectedUsernamePrefix) &&
            user.UserName.Length <= _options.MaxUserNameLength)), Times.Once);
    }
}