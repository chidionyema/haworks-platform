using System.Security.Claims;
using Haworks.Identity.Api.Controllers;
using Haworks.Identity.Application;
using Haworks.Identity.Application.Commands.Auth;
using Haworks.Identity.Application.Commands.Auth;
using Haworks.Identity.Application.DTOs;
using Haworks.Identity.Application.Options;
using Haworks.Identity.Application.Queries;
using Haworks.Identity.Domain;
using Haworks.BuildingBlocks.Common;
using Haworks.BuildingBlocks.Testing;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Xunit.Abstractions;
using Xunit;

namespace Haworks.Identity.UnitTests.Controllers;

public class ExternalAuthenticationControllerTests : TestBase
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<SignInManager<User>> _signInManagerMock;
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly SecurityOptions _securityOptions;
    private readonly ExternalAuthenticationController _controller;

    public ExternalAuthenticationControllerTests(ITestOutputHelper output) : base(output)
    {
        _mediatorMock = new Mock<IMediator>();

        var userStoreMock = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var contextAccessorMock = new Mock<IHttpContextAccessor>();
        var userPrincipalFactoryMock = new Mock<IUserClaimsPrincipalFactory<User>>();
        var signInManagerMock = new Mock<SignInManager<User>>(
            _userManagerMock.Object,
            contextAccessorMock.Object,
            userPrincipalFactoryMock.Object,
            null!, null!, null!, null!);
        _signInManagerMock = signInManagerMock;

        _securityOptions = new SecurityOptions
        {
            AllowedRedirectHosts = ["localhost", "example.com"]
        };

        var securityOptionsMock = new Mock<IOptions<SecurityOptions>>();
        securityOptionsMock.Setup(o => o.Value).Returns(_securityOptions);

        var loggerMock = new Mock<Microsoft.Extensions.Logging.ILogger<ExternalAuthenticationController>>();

        _controller = new ExternalAuthenticationController(
            _mediatorMock.Object,
            _signInManagerMock.Object,
            securityOptionsMock.Object,
            loggerMock.Object);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Fact]
    public async Task Callback_WhenMediatorReturnsSuccess_ReturnsOkWithAuthResponse()
    {
        // Arrange
        var authResponse = new AuthResponseDto
        {
            Token = "jwt-token",
            RefreshToken = "refresh-token",
            Expires = DateTime.UtcNow.AddMinutes(15),
            Username = "testuser",
            Email = "test@example.com",
            UserId = "user-123",
            Message = "Success"
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ExternalLoginCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(authResponse));

        // Act
        var result = await _controller.Callback(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        Assert.NotNull(response);

        var token = GetPropertyValue<string>(response, "token");
        var refreshToken = GetPropertyValue<string>(response, "refreshToken");
        var user = GetPropertyValue<object>(response, "user");

        Assert.Equal("jwt-token", token);
        Assert.Equal("refresh-token", refreshToken);
        Assert.NotNull(user);

        var userId = GetPropertyValue<string>(user, "id");
        var userName = GetPropertyValue<string>(user, "userName");
        var email = GetPropertyValue<string>(user, "email");

        Assert.Equal("user-123", userId);
        Assert.Equal("testuser", userName);
        Assert.Equal("test@example.com", email);
    }

    [Fact]
    public async Task Callback_WhenMediatorReturnsFailure_ReturnsErrorResult()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ExternalLoginCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AuthResponseDto>(Error.Internal("ExternalLogin.Failed", "External login failed")));

        // Act
        var result = await _controller.Callback(CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task LinkExternalLogin_WhenRequiresChallenge_ReturnsChallengeResult()
    {
        // Arrange
        var userId = "user-123";
        var provider = "Google";

        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)]));

        var loginInfo = CreateExternalLoginInfo(provider, "google-user-123");

        _signInManagerMock
            .Setup(s => s.GetExternalLoginInfoAsync(null))
            .ReturnsAsync(loginInfo);

        var linkResult = new LinkExternalLoginResult(true, "Challenge required");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<LinkExternalLoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(linkResult));

        var properties = new AuthenticationProperties();
        _signInManagerMock
            .Setup(s => s.ConfigureExternalAuthenticationProperties(provider, It.IsAny<string>(), userId))
            .Returns(properties);

        // Act
        var result = await _controller.LinkExternalLogin(provider, CancellationToken.None);

        // Assert
        var challengeResult = Assert.IsType<ChallengeResult>(result);
        Assert.Contains(provider, challengeResult.AuthenticationSchemes);
    }

    [Fact]
    public async Task LinkExternalLogin_WhenSuccessfulWithoutChallenge_ReturnsOk()
    {
        // Arrange
        var userId = "user-123";
        var provider = "Google";

        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)]));

        var loginInfo = CreateExternalLoginInfo(provider, "google-user-123");

        _signInManagerMock
            .Setup(s => s.GetExternalLoginInfoAsync(null))
            .ReturnsAsync(loginInfo);

        var linkResult = new LinkExternalLoginResult(false, "Login linked successfully");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<LinkExternalLoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(linkResult));

        // Act
        var result = await _controller.LinkExternalLogin(provider, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        Assert.NotNull(response);

        var message = GetPropertyValue<string>(response, "Message");
        Assert.Equal("Login linked successfully", message);
    }

    [Fact]
    public async Task LinkExternalLogin_WhenMediatorFails_ReturnsErrorResult()
    {
        // Arrange
        var userId = "user-123";
        var provider = "Google";

        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)]));

        var loginInfo = CreateExternalLoginInfo(provider, "google-user-123");

        _signInManagerMock
            .Setup(s => s.GetExternalLoginInfoAsync(null))
            .ReturnsAsync(loginInfo);

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<LinkExternalLoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<LinkExternalLoginResult>(Error.Internal("Link.Failed", "Link failed")));

        // Act
        var result = await _controller.LinkExternalLogin(provider, CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task LinkCallback_WhenExternalLoginInfoIsNull_ReturnsBadRequest()
    {
        // Arrange
        _signInManagerMock
            .Setup(s => s.GetExternalLoginInfoAsync(null))
            .ReturnsAsync((ExternalLoginInfo?)null);

        // Act
        var result = await _controller.LinkCallback(CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Error getting external login information", badRequestResult.Value);
    }

    [Fact]
    public async Task LinkCallback_WhenSuccessful_ReturnsOkWithMessage()
    {
        // Arrange
        var loginInfo = CreateExternalLoginInfo("Google", "google-user-123");

        _signInManagerMock
            .Setup(s => s.GetExternalLoginInfoAsync(null))
            .ReturnsAsync(loginInfo);

        var linkResult = new LinkExternalLoginResult
        {
            RequiresChallenge = false,
            Message = "Successfully linked"
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<LinkExternalLoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(linkResult));

        // Act
        var result = await _controller.LinkCallback(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        Assert.NotNull(response);

        var message = GetPropertyValue<string>(response, "Message");
        Assert.Contains("Google", message);
    }

    [Fact]
    public async Task RemoveExternalLogin_WhenSuccessful_ReturnsOkWithMessage()
    {
        // Arrange
        var userId = "user-123";
        var provider = "Google";

        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)]));

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UnlinkExternalLoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _controller.RemoveExternalLogin(provider, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        Assert.NotNull(response);

        var message = GetPropertyValue<string>(response, "Message");
        Assert.Contains("Google", message);
        Assert.Contains("removed", message);
    }

    [Fact]
    public async Task RemoveExternalLogin_WhenMediatorFails_ReturnsErrorResult()
    {
        // Arrange
        var userId = "user-123";
        var provider = "Google";

        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)]));

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UnlinkExternalLoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.Internal("Unlink.Failed", "Unlink failed")));

        // Act
        var result = await _controller.RemoveExternalLogin(provider, CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetUserLogins_WhenSuccessful_ReturnsOkWithLogins()
    {
        // Arrange
        var userId = "user-123";

        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)]));

        var userLogins = new List<ExternalLoginInfoDto>
        {
            new("Google", "Google"),
            new("Microsoft", "Microsoft")
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetUserLoginsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(userLogins));

        // Act
        var result = await _controller.GetUserLogins(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        Assert.NotNull(response);

        var logins = GetPropertyValue<object>(response, "Logins");
        Assert.NotNull(logins);
        Assert.Equal(userLogins, logins);
    }

    [Fact]
    public async Task GetUserLogins_WhenMediatorFails_ReturnsErrorResult()
    {
        // Arrange
        var userId = "user-123";

        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)]));

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetUserLoginsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<List<ExternalLoginInfoDto>>(Error.Internal("Query.Failed", "Query failed")));

        // Act
        var result = await _controller.GetUserLogins(CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData("Google")]
    [InlineData("Microsoft")]
    [InlineData("Facebook")]
    public async Task LinkExternalLogin_WithDifferentProviders_UsesCorrectProvider(string provider)
    {
        // Arrange
        var userId = "user-123";

        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)]));

        var loginInfo = CreateExternalLoginInfo(provider, "provider-user-123");

        _signInManagerMock
            .Setup(s => s.GetExternalLoginInfoAsync(null))
            .ReturnsAsync(loginInfo);

        var linkResult = new LinkExternalLoginResult
        {
            RequiresChallenge = false,
            Message = "Success"
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<LinkExternalLoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(linkResult));

        // Act
        await _controller.LinkExternalLogin(provider, CancellationToken.None);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<LinkExternalLoginCommand>(cmd => cmd.Provider == provider),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ExternalLoginInfo CreateExternalLoginInfo(string provider, string providerKey)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, providerKey)]);
        var principal = new ClaimsPrincipal(identity);

        return new ExternalLoginInfo(principal, provider, providerKey, provider);
    }

    private static T GetPropertyValue<T>(object obj, string propertyName)
    {
        var property = obj.GetType().GetProperty(propertyName);
        if (property == null)
            throw new ArgumentException($"Property '{propertyName}' not found");

        var value = property.GetValue(obj);
        return value is T typedValue ? typedValue : throw new InvalidCastException($"Property '{propertyName}' is not of type {typeof(T)}");
    }
}

