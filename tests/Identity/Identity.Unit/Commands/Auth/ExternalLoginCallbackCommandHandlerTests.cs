using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Haworks.Identity.Application;
using Haworks.Identity.Application.DTOs;
using Haworks.Identity.Application.Interfaces;
using Haworks.Identity.Domain;
using Haworks.BuildingBlocks.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit.Abstractions;
using Xunit;

namespace Haworks.Identity.UnitTests.Commands.Auth;

public class ExternalLoginCallbackCommandHandlerTests : TestBase
{
    private readonly Mock<SignInManager<User>> _signInManagerMock;
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock;
    private readonly Mock<IRefreshTokenService> _refreshTokenServiceMock;
    private readonly Mock<ILogger<ExternalLoginCallbackCommandHandler>> _loggerMock;
    private readonly ExternalLoginCallbackCommandHandler _handler;
    private readonly ExternalLoginOptions _options;

    public ExternalLoginCallbackCommandHandlerTests(ITestOutputHelper output) : base(output)
    {
        var userStoreMock = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var contextAccessorMock = new Mock<IHttpContextAccessor>();
        var userPrincipalFactoryMock = new Mock<IUserClaimsPrincipalFactory<User>>();
        _signInManagerMock = new Mock<SignInManager<User>>(
            _userManagerMock.Object,
            contextAccessorMock.Object,
            userPrincipalFactoryMock.Object,
            null!, null!, null!, null!);

        _jwtTokenServiceMock = new Mock<IJwtTokenService>();
        _refreshTokenServiceMock = new Mock<IRefreshTokenService>();
        _loggerMock = new Mock<ILogger<ExternalLoginCallbackCommandHandler>>();

        _options = new ExternalLoginOptions
        {
            TrustedEmailProviders = ["Google", "Microsoft", "Apple"],
            AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_",
            MaxUserNameLength = 50
        };

        _handler = new ExternalLoginCallbackCommandHandler(
            _signInManagerMock.Object,
            _userManagerMock.Object,
            _jwtTokenServiceMock.Object,
            _refreshTokenServiceMock.Object,
            _loggerMock.Object,
            Options.Create(_options));
    }

    [Fact]
    public async Task Handle_WhenExternalLoginInfoIsNull_ReturnsFailure()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext, "test-key");

        _signInManagerMock
            .Setup(s => s.GetExternalLoginInfoAsync(null))
            .ReturnsAsync((ExternalLoginInfo?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("External login info not found", result.Error);
    }

    [Fact]
    public async Task Handle_WhenEmailIsUnverifiedFromUntrustedProvider_ReturnsFailure()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext, "test-key");

        var loginInfo = CreateExternalLoginInfo(
            provider: "Facebook", // Not in trusted providers
            claims: [
                new Claim(ClaimTypes.Email, "test@example.com"),
                new Claim("email_verified", "false")
            ]);

        _signInManagerMock
            .Setup(s => s.GetExternalLoginInfoAsync(null))
            .ReturnsAsync(loginInfo);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Email address must be verified", result.Error);
    }

    [Fact]
    public async Task Handle_WhenTrustedProviderWithoutEmailVerified_Succeeds()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext, "test-key");

        var loginInfo = CreateExternalLoginInfo(
            provider: "Google", // Trusted provider
            claims: [
                new Claim(ClaimTypes.Email, "test@example.com"),
                new Claim(ClaimTypes.Name, "Test User"),
                new Claim("sub", "google-user-123")
            ]);

        _signInManagerMock
            .Setup(s => s.GetExternalLoginInfoAsync(null))
            .ReturnsAsync(loginInfo);

        _signInManagerMock
            .Setup(s => s.ExternalLoginSignInAsync("Google", "google-user-123", false, false))
            .ReturnsAsync(SignInResult.Failed);

        _userManagerMock
            .Setup(u => u.FindByEmailAsync("test@example.com"))
            .ReturnsAsync((User?)null);

        var createdUser = CreateUser("test@example.com", "testuser");
        _userManagerMock
            .Setup(u => u.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Success)
            .Callback<User>(user => user.Id = "new-user-id");

        _userManagerMock
            .Setup(u => u.AddLoginAsync(It.IsAny<User>(), It.IsAny<UserLoginInfo>()))
            .ReturnsAsync(IdentityResult.Success);

        _jwtTokenServiceMock
            .Setup(j => j.GenerateTokenAsync(It.IsAny<User>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JwtSecurityToken());

        _refreshTokenServiceMock
            .Setup(r => r.GenerateRefreshTokenAsync("new-user-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshToken());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotNull(result.Value.Token);
        Assert.NotNull(result.Value.RefreshToken);
    }

    [Fact]
    public async Task Handle_WhenUserExistsAndSignInSucceeds_ReturnsSuccess()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext, "test-key");

        var loginInfo = CreateExternalLoginInfo(
            provider: "Google",
            claims: [
                new Claim(ClaimTypes.Email, "existing@example.com"),
                new Claim("sub", "google-user-123")
            ]);

        var existingUser = CreateUser("existing@example.com", "existinguser");
        existingUser.Id = "existing-user-id";

        _signInManagerMock
            .Setup(s => s.GetExternalLoginInfoAsync(null))
            .ReturnsAsync(loginInfo);

        _signInManagerMock
            .Setup(s => s.ExternalLoginSignInAsync("Google", "google-user-123", false, false))
            .ReturnsAsync(SignInResult.Success);

        _userManagerMock
            .Setup(u => u.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(existingUser);

        _jwtTokenServiceMock
            .Setup(j => j.GenerateTokenAsync(existingUser))
            .ReturnsAsync("jwt-token");

        _refreshTokenServiceMock
            .Setup(r => r.CreateRefreshTokenAsync("existing-user-id"))
            .ReturnsAsync("refresh-token");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotNull(result.Value.Token);
        Assert.Equal("existing@example.com", result.Value.Email);
        Assert.Equal("existinguser", result.Value.Username);
    }

    [Fact]
    public async Task Handle_WhenEmailExistsButNoExternalLogin_LinksAccount()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext, "test-key");

        var loginInfo = CreateExternalLoginInfo(
            provider: "Google",
            claims: [
                new Claim(ClaimTypes.Email, "existing@example.com"),
                new Claim("sub", "google-user-123")
            ]);

        var existingUser = CreateUser("existing@example.com", "existinguser");
        existingUser.Id = "existing-user-id";

        _signInManagerMock
            .Setup(s => s.GetExternalLoginInfoAsync(null))
            .ReturnsAsync(loginInfo);

        _signInManagerMock
            .Setup(s => s.ExternalLoginSignInAsync("Google", "google-user-123", false, false))
            .ReturnsAsync(SignInResult.Failed);

        _userManagerMock
            .Setup(u => u.FindByEmailAsync("existing@example.com"))
            .ReturnsAsync(existingUser);

        _userManagerMock
            .Setup(u => u.AddLoginAsync(existingUser, It.IsAny<UserLoginInfo>()))
            .ReturnsAsync(IdentityResult.Success);

        _jwtTokenServiceMock
            .Setup(j => j.GenerateTokenAsync(existingUser))
            .ReturnsAsync("jwt-token");

        _refreshTokenServiceMock
            .Setup(r => r.CreateRefreshTokenAsync("existing-user-id"))
            .ReturnsAsync("refresh-token");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);

        // Verify AddLoginAsync was called to link the external login
        _userManagerMock.Verify(u => u.AddLoginAsync(
            existingUser,
            It.Is<UserLoginInfo>(info =>
                info.LoginProvider == "Google" &&
                info.ProviderKey == "google-user-123")),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUserCreationFails_ReturnsFailure()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext, "test-key");

        var loginInfo = CreateExternalLoginInfo(
            provider: "Google",
            claims: [
                new Claim(ClaimTypes.Email, "test@example.com"),
                new Claim("sub", "google-user-123")
            ]);

        _signInManagerMock
            .Setup(s => s.GetExternalLoginInfoAsync(null))
            .ReturnsAsync(loginInfo);

        _signInManagerMock
            .Setup(s => s.ExternalLoginSignInAsync("Google", "google-user-123", false, false))
            .ReturnsAsync(SignInResult.Failed);

        _userManagerMock
            .Setup(u => u.FindByEmailAsync("test@example.com"))
            .ReturnsAsync((User?)null);

        _userManagerMock
            .Setup(u => u.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "User creation failed" }));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("User creation failed", result.Error.Message);
    }

    [Theory]
    [InlineData("Google")]
    [InlineData("Microsoft")]
    [InlineData("Apple")]
    public async Task Handle_TrustedProvidersDoNotRequireEmailVerified(string provider)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext, "test-key");

        var loginInfo = CreateExternalLoginInfo(
            provider: provider,
            claims: [
                new Claim(ClaimTypes.Email, "test@example.com"),
                new Claim("sub", "provider-user-123")
                // Note: no email_verified claim
            ]);

        _signInManagerMock
            .Setup(s => s.GetExternalLoginInfoAsync(null))
            .ReturnsAsync(loginInfo);

        _signInManagerMock
            .Setup(s => s.ExternalLoginSignInAsync(provider, "provider-user-123", false, false))
            .ReturnsAsync(SignInResult.Failed);

        _userManagerMock
            .Setup(u => u.FindByEmailAsync("test@example.com"))
            .ReturnsAsync((User?)null);

        var createdUser = CreateUser("test@example.com", "testuser");
        _userManagerMock
            .Setup(u => u.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock
            .Setup(u => u.AddLoginAsync(It.IsAny<User>(), It.IsAny<UserLoginInfo>()))
            .ReturnsAsync(IdentityResult.Success);

        _jwtTokenServiceMock
            .Setup(j => j.GenerateTokenAsync(It.IsAny<User>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JwtSecurityToken());

        _refreshTokenServiceMock
            .Setup(r => r.CreateRefreshTokenAsync(It.IsAny<string>()))
            .ReturnsAsync("refresh-token");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
    }

    private static ExternalLoginInfo CreateExternalLoginInfo(string provider, Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);
        var providerKey = claims.FirstOrDefault(c => c.Type == "sub")?.Value ?? "default-key";

        return new ExternalLoginInfo(principal, provider, providerKey, provider);
    }

    private static User CreateUser(string email, string username)
    {
        return new User
        {
            Email = email,
            UserName = username,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = username.ToUpperInvariant(),
            IsActive = true
        };
    }
}