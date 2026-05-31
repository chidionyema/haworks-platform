using System.Security.Claims;
using Haworks.Identity.Application;
using Haworks.Identity.Application.Interfaces;
using Haworks.Identity.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Haworks.BuildingBlocks.Common;

namespace Haworks.Identity.Unit.Commands.Auth;

public class ExternalLoginCallbackCommandHandlerTests
{
    private readonly Mock<SignInManager<User>> _signInManagerMock;
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock;
    private readonly Mock<IRefreshTokenService> _refreshTokenServiceMock;
    private readonly Mock<ILogger<ExternalLoginCallbackCommandHandler>> _loggerMock;
    private readonly ExternalLoginOptions _options;

    public ExternalLoginCallbackCommandHandlerTests()
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
    }

    private ExternalLoginCallbackCommandHandler CreateHandler() =>
        new(_signInManagerMock.Object, _userManagerMock.Object, _jwtTokenServiceMock.Object,
            _refreshTokenServiceMock.Object, _loggerMock.Object, Options.Create(_options));

    [Fact]
    public async Task Handle_NullHttpContext_ReturnsInvalidContextError()
    {
        // Arrange
        var command = new ExternalLoginCallbackCommand(null!);
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(Error.Auth.InvalidContext, result.Error);
    }

    [Fact]
    public async Task Handle_NoExternalLoginInfo_ReturnsExternalLoginFailedError()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext);
        var handler = CreateHandler();

        _signInManagerMock.Setup(s => s.GetExternalLoginInfoAsync())
            .ReturnsAsync((ExternalLoginInfo)null!);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(Error.Auth.ExternalLoginFailed, result.Error);
    }

    [Fact]
    public async Task Handle_EmptyProviderKey_ReturnsInvalidProviderKeyError()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext);
        var handler = CreateHandler();

        var loginInfo = new ExternalLoginInfo(
            new ClaimsPrincipal(),
            "Google",
            "",  // Empty provider key
            "Google"
        );

        _signInManagerMock.Setup(s => s.GetExternalLoginInfoAsync())
            .ReturnsAsync(loginInfo);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(Error.Auth.InvalidProviderKey, result.Error);
    }

    [Fact]
    public async Task Handle_ExistingUserSignInSuccessful_ReturnsAuthResponse()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext);
        var handler = CreateHandler();

        var user = new User { Id = Guid.NewGuid().ToString(), Email = "test@example.com", UserName = "testuser" };
        var loginInfo = CreateValidLoginInfo("Google", "123456", "test@example.com");

        _signInManagerMock.Setup(s => s.GetExternalLoginInfoAsync())
            .ReturnsAsync(loginInfo);
        _signInManagerMock.Setup(s => s.ExternalLoginSignInAsync("Google", "123456", false, false))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);
        _userManagerMock.Setup(u => u.FindByLoginAsync("Google", "123456"))
            .ReturnsAsync(user);

        SetupTokenGeneration(user);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, result.Value.UserId);
        Assert.Equal(user.Email, result.Value.Email);
    }

    [Fact]
    public async Task Handle_NoEmailClaim_ReturnsMissingEmailError()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext);
        var handler = CreateHandler();

        var claims = new[] { new Claim(ClaimTypes.Name, "testuser") }; // No email claim
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var loginInfo = new ExternalLoginInfo(principal, "Google", "123456", "Google");

        _signInManagerMock.Setup(s => s.GetExternalLoginInfoAsync())
            .ReturnsAsync(loginInfo);
        _signInManagerMock.Setup(s => s.ExternalLoginSignInAsync("Google", "123456", false, false))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(Error.Auth.MissingEmail, result.Error);
    }

    [Fact]
    public async Task Handle_UnverifiedEmailFromUntrustedProvider_ReturnsUnverifiedEmailError()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext);
        var handler = CreateHandler();

        var claims = new[]
        {
            new Claim(ClaimTypes.Email, "test@example.com"),
            new Claim("email_verified", "false")  // Explicitly unverified
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var loginInfo = new ExternalLoginInfo(principal, "UnknownProvider", "123456", "Unknown Provider");

        _signInManagerMock.Setup(s => s.GetExternalLoginInfoAsync())
            .ReturnsAsync(loginInfo);
        _signInManagerMock.Setup(s => s.ExternalLoginSignInAsync("UnknownProvider", "123456", false, false))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(Error.Auth.UnverifiedEmail, result.Error);
    }

    [Fact]
    public async Task Handle_TrustedProvider_AcceptsEmailWithoutVerificationClaim()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext);
        var handler = CreateHandler();

        var loginInfo = CreateValidLoginInfo("Google", "123456", "test@example.com"); // Trusted provider, no email_verified claim

        _signInManagerMock.Setup(s => s.GetExternalLoginInfoAsync())
            .ReturnsAsync(loginInfo);
        _signInManagerMock.Setup(s => s.ExternalLoginSignInAsync("Google", "123456", false, false))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        // No existing user
        _userManagerMock.Setup(u => u.FindByLoginAsync("Google", "123456"))
            .ReturnsAsync((User)null!);
        _userManagerMock.Setup(u => u.FindByEmailAsync("test@example.com"))
            .ReturnsAsync((User)null!);

        // Setup new user creation
        _userManagerMock.Setup(u => u.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(u => u.AddLoginAsync(It.IsAny<User>(), It.IsAny<ExternalLoginInfo>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(u => u.AddToRoleAsync(It.IsAny<User>(), "ContentUploader"))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(u => u.AddClaimAsync(It.IsAny<User>(), It.IsAny<Claim>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(u => u.FindByNameAsync(It.IsAny<string>()))
            .ReturnsAsync((User)null!);

        SetupTokenGeneration(null);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_ExistingUserByEmail_LinksExternalLoginSuccessfully()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext);
        var handler = CreateHandler();

        var existingUser = new User { Id = Guid.NewGuid().ToString(), Email = "test@example.com", UserName = "existinguser" };
        var loginInfo = CreateValidLoginInfo("Google", "123456", "test@example.com");

        _signInManagerMock.Setup(s => s.GetExternalLoginInfoAsync())
            .ReturnsAsync(loginInfo);
        _signInManagerMock.Setup(s => s.ExternalLoginSignInAsync("Google", "123456", false, false))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        // No existing external login, but existing user by email
        _userManagerMock.Setup(u => u.FindByLoginAsync("Google", "123456"))
            .ReturnsAsync((User)null!);
        _userManagerMock.Setup(u => u.FindByEmailAsync("test@example.com"))
            .ReturnsAsync(existingUser);
        _userManagerMock.Setup(u => u.AddLoginAsync(existingUser, loginInfo))
            .ReturnsAsync(IdentityResult.Success);

        SetupTokenGeneration(existingUser);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(existingUser.Id, result.Value.UserId);
        Assert.Equal(existingUser.Email, result.Value.Email);
    }

    [Fact]
    public async Task Handle_DuplicateEmailRaceCondition_RetriesAndLinksSuccessfully()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext);
        var handler = CreateHandler();

        var loginInfo = CreateValidLoginInfo("Google", "123456", "test@example.com");
        var racingUser = new User { Id = Guid.NewGuid().ToString(), Email = "test@example.com", UserName = "racinguser" };

        _signInManagerMock.Setup(s => s.GetExternalLoginInfoAsync())
            .ReturnsAsync(loginInfo);
        _signInManagerMock.Setup(s => s.ExternalLoginSignInAsync("Google", "123456", false, false))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        // No existing login or user initially
        _userManagerMock.Setup(u => u.FindByLoginAsync("Google", "123456"))
            .ReturnsAsync((User)null!);
        _userManagerMock.Setup(u => u.FindByEmailAsync("test@example.com"))
            .ReturnsAsync((User)null!);

        // Creation fails with duplicate email (race condition)
        var duplicateEmailError = IdentityError.DefaultError();
        duplicateEmailError.Code = "DuplicateEmail";
        duplicateEmailError.Description = "Email already exists";

        _userManagerMock.Setup(u => u.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Failed(duplicateEmailError));

        // On retry, user is found and linking succeeds
        _userManagerMock.Setup(u => u.FindByEmailAsync("test@example.com"))
            .ReturnsAsync(racingUser);
        _userManagerMock.Setup(u => u.AddLoginAsync(racingUser, loginInfo))
            .ReturnsAsync(IdentityResult.Success);

        SetupTokenGeneration(racingUser);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(racingUser.Id, result.Value.UserId);
    }

    [Fact]
    public async Task Handle_DuplicateUsernameRaceCondition_RetriesWithNewUsername()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var command = new ExternalLoginCallbackCommand(httpContext);
        var handler = CreateHandler();

        var loginInfo = CreateValidLoginInfo("Google", "123456", "test@example.com");

        _signInManagerMock.Setup(s => s.GetExternalLoginInfoAsync())
            .ReturnsAsync(loginInfo);
        _signInManagerMock.Setup(s => s.ExternalLoginSignInAsync("Google", "123456", false, false))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        _userManagerMock.Setup(u => u.FindByLoginAsync("Google", "123456"))
            .ReturnsAsync((User)null!);
        _userManagerMock.Setup(u => u.FindByEmailAsync("test@example.com"))
            .ReturnsAsync((User)null!);

        // First creation fails with duplicate username
        var duplicateUsernameError = IdentityError.DefaultError();
        duplicateUsernameError.Code = "DuplicateUserName";
        duplicateUsernameError.Description = "Username already exists";

        _userManagerMock.SetupSequence(u => u.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Failed(duplicateUsernameError))
            .ReturnsAsync(IdentityResult.Success);

        // Username checks for uniqueness
        _userManagerMock.SetupSequence(u => u.FindByNameAsync(It.IsAny<string>()))
            .ReturnsAsync(new User()) // First username taken
            .ReturnsAsync((User)null!); // Second username available

        _userManagerMock.Setup(u => u.AddLoginAsync(It.IsAny<User>(), It.IsAny<ExternalLoginInfo>()))
            .ReturnsAsync(IdentityResult.Success);

        SetupTokenGeneration(null);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
    }

    private ExternalLoginInfo CreateValidLoginInfo(string provider, string providerKey, string email)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, "Test User")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));
        return new ExternalLoginInfo(principal, provider, providerKey, provider);
    }

    private void SetupTokenGeneration(User? user)
    {
        var mockUser = user ?? new User { Id = Guid.NewGuid().ToString(), Email = "test@example.com", UserName = "testuser" };
        var mockToken = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken();
        var mockRefreshToken = new RefreshToken { Token = "refresh-token", UserId = mockUser.Id, Expires = DateTime.UtcNow.AddDays(7) };

        _jwtTokenServiceMock.Setup(j => j.GenerateTokenAsync(It.IsAny<User>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockToken);
        _refreshTokenServiceMock.Setup(r => r.GenerateRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockRefreshToken);
        _jwtTokenServiceMock.Setup(j => j.SetSecureCookie(It.IsAny<HttpContext>(), It.IsAny<System.IdentityModel.Tokens.Jwt.JwtSecurityToken>()));
    }
}