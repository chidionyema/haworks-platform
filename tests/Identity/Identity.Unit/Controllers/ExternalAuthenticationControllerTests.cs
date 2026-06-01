using Haworks.Identity.Api.Controllers;
using Haworks.Identity.Application;
using Haworks.Identity.Application.DTOs;
using Haworks.Identity.Application.Options;
using Haworks.Identity.Domain;
using Haworks.BuildingBlocks.Common;
using Haworks.BuildingBlocks.Testing;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;
using Xunit.Abstractions;
using Xunit;

namespace Haworks.Identity.UnitTests.Controllers;

public class ExternalAuthenticationControllerTests : TestBase
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<SignInManager<User>> _signInManagerMock;
    private readonly Mock<IOptions<SecurityOptions>> _securityOptionsMock;
    private readonly Mock<ILogger<ExternalAuthenticationController>> _loggerMock;
    private readonly ExternalAuthenticationController _controller;
    private readonly SecurityOptions _securityOptions;

    public ExternalAuthenticationControllerTests(ITestOutputHelper output) : base(output)
    {
        _mediatorMock = new Mock<IMediator>();
        _signInManagerMock = new Mock<SignInManager<User>>(
            Mock.Of<UserManager<User>>(),
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<User>>(),
            null, null, null, null);

        _securityOptions = new SecurityOptions
        {
            AllowedRedirectHosts = new[] { "localhost", "example.com" }
        };
        _securityOptionsMock = new Mock<IOptions<SecurityOptions>>();
        _securityOptionsMock.Setup(x => x.Value).Returns(_securityOptions);

        _loggerMock = new Mock<ILogger<ExternalAuthenticationController>>();

        _controller = new ExternalAuthenticationController(
            _mediatorMock.Object,
            _signInManagerMock.Object,
            _securityOptionsMock.Object,
            _loggerMock.Object);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Fact]
    public async Task Challenge_WithValidProvider_ReturnsChallenge()
    {
        var provider = "Google";
        var redirectUrl = "https://localhost/callback";
        var authSchemes = new[]
        {
            new AuthenticationScheme("Google", "Google", typeof(IAuthenticationHandler))
        };

        _signInManagerMock.Setup(s => s.GetExternalAuthenticationSchemesAsync())
            .ReturnsAsync(authSchemes);
        _signInManagerMock.Setup(s => s.ConfigureExternalAuthenticationProperties(provider, redirectUrl))
            .Returns(new AuthenticationProperties());

        var result = await _controller.Challenge(provider, redirectUrl, CancellationToken.None);

        Assert.IsType<ChallengeResult>(result);
    }

    [Fact]
    public async Task Challenge_WithInvalidProvider_ReturnsBadRequest()
    {
        var provider = "InvalidProvider";
        var authSchemes = new[]
        {
            new AuthenticationScheme("Google", "Google", typeof(IAuthenticationHandler))
        };

        _signInManagerMock.Setup(s => s.GetExternalAuthenticationSchemesAsync())
            .ReturnsAsync(authSchemes);

        var result = await _controller.Challenge(provider, null, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Provider 'InvalidProvider' is not supported", badRequestResult.Value);
    }

    [Fact]
    public async Task Challenge_WithInvalidRedirectUrl_UsesDefaultCallback()
    {
        var provider = "Google";
        var invalidUrl = "javascript:alert(1)";
        var authSchemes = new[]
        {
            new AuthenticationScheme("Google", "Google", typeof(IAuthenticationHandler))
        };

        _signInManagerMock.Setup(s => s.GetExternalAuthenticationSchemesAsync())
            .ReturnsAsync(authSchemes);
        _signInManagerMock.Setup(s => s.ConfigureExternalAuthenticationProperties(provider, It.IsAny<string>()))
            .Returns(new AuthenticationProperties());

        var result = await _controller.Challenge(provider, invalidUrl, CancellationToken.None);

        Assert.IsType<ChallengeResult>(result);
        _signInManagerMock.Verify(s => s.ConfigureExternalAuthenticationProperties(provider,
            It.Is<string>(url => !url.Contains("javascript"))), Times.Once);
    }

    [Fact]
    public async Task Callback_WithSuccessfulLogin_ReturnsOk()
    {
        var authResponseDto = new AuthResponseDto
        {
            Token = "jwt-token",
            RefreshToken = "refresh-token",
            Expires = DateTime.UtcNow.AddHours(1),
            Username = "testuser",
            Email = "test@example.com",
            UserId = "user-123",
            Message = "Login successful"
        };

        _mediatorMock.Setup(m => m.Send(It.IsAny<ExternalLoginCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(authResponseDto));

        var result = await _controller.Callback(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        dynamic response = okResult.Value!;
        Assert.Equal("jwt-token", response.GetType().GetProperty("token")!.GetValue(response));
        Assert.Equal("refresh-token", response.GetType().GetProperty("refreshToken")!.GetValue(response));
        Assert.Equal("user-123", response.GetType().GetProperty("user")!.GetValue(response)!.GetType().GetProperty("id")!.GetValue(response.GetType().GetProperty("user")!.GetValue(response)));
    }

    [Fact]
    public async Task Callback_WithFailedLogin_ReturnsBadRequest()
    {
        _mediatorMock.Setup(m => m.Send(It.IsAny<ExternalLoginCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AuthResponseDto>(
                Error.Auth.ExternalLoginFailed));

        var result = await _controller.Callback(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetAvailableProviders_ReturnsProviders()
    {
        var providers = new[] { "Google", "Microsoft", "Facebook" };

        _mediatorMock.Setup(m => m.Send(It.IsAny<GetAvailableProvidersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(providers));

        var result = await _controller.GetAvailableProviders(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        dynamic response = okResult.Value!;
        var returnedProviders = (string[])response.GetType().GetProperty("providers")!.GetValue(response)!;
        Assert.Equal(providers, returnedProviders);
    }

    [Fact]
    public async Task LinkExternalLogin_WithRequiredChallenge_ReturnsChallenge()
    {
        var provider = "Google";
        var userId = "user-123";
        var linkResult = new LinkExternalLoginResult
        {
            RequiresChallenge = true,
            Message = "Challenge required"
        };

        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        }));

        _mediatorMock.Setup(m => m.Send(It.IsAny<LinkExternalLoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(linkResult));

        _signInManagerMock.Setup(s => s.ConfigureExternalAuthenticationProperties(provider, It.IsAny<string>(), userId))
            .Returns(new AuthenticationProperties());

        var result = await _controller.LinkExternalLogin(provider, CancellationToken.None);

        Assert.IsType<ChallengeResult>(result);
    }

    [Fact]
    public async Task LinkExternalLogin_WithoutChallenge_ReturnsOk()
    {
        var provider = "Google";
        var userId = "user-123";
        var linkResult = new LinkExternalLoginResult
        {
            RequiresChallenge = false,
            Message = "Successfully linked"
        };

        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        }));

        _mediatorMock.Setup(m => m.Send(It.IsAny<LinkExternalLoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(linkResult));

        var result = await _controller.LinkExternalLogin(provider, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        dynamic response = okResult.Value!;
        Assert.Equal("Successfully linked", response.GetType().GetProperty("Message")!.GetValue(response));
    }

    [Fact]
    public async Task LinkCallback_WithValidInfo_ReturnsOk()
    {
        var loginInfo = new ExternalLoginInfo(
            new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-123")
            })),
            "Google",
            "google-key",
            "Google");

        var linkResult = new LinkExternalLoginResult
        {
            RequiresChallenge = false,
            Message = "Successfully linked Google login to your account"
        };

        _signInManagerMock.Setup(s => s.GetExternalLoginInfoAsync())
            .ReturnsAsync(loginInfo);

        _mediatorMock.Setup(m => m.Send(It.IsAny<LinkExternalLoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(linkResult));

        var result = await _controller.LinkCallback(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        dynamic response = okResult.Value!;
        Assert.Equal("Successfully linked Google login to your account", response.GetType().GetProperty("Message")!.GetValue(response));
    }

    [Fact]
    public async Task LinkCallback_WithNoLoginInfo_ReturnsBadRequest()
    {
        _signInManagerMock.Setup(s => s.GetExternalLoginInfoAsync())
            .ReturnsAsync((ExternalLoginInfo?)null);

        var result = await _controller.LinkCallback(CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Error getting external login information", badRequestResult.Value);
    }

    [Fact]
    public async Task RemoveExternalLogin_WithValidProvider_ReturnsOk()
    {
        var provider = "Google";
        var userId = "user-123";

        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        }));

        _mediatorMock.Setup(m => m.Send(It.IsAny<UnlinkExternalLoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success("Successfully removed Google login from your account"));

        var result = await _controller.RemoveExternalLogin(provider, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        dynamic response = okResult.Value!;
        Assert.Equal("Successfully removed Google login from your account", response.GetType().GetProperty("Message")!.GetValue(response));
    }

    [Fact]
    public async Task GetUserLogins_WithValidUser_ReturnsLogins()
    {
        var userId = "user-123";
        var logins = new[]
        {
            new UserLoginDto { Provider = "Google", ProviderKey = "google-key" },
            new UserLoginDto { Provider = "Microsoft", ProviderKey = "ms-key" }
        };

        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        }));

        _mediatorMock.Setup(m => m.Send(It.IsAny<GetUserLoginsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(logins));

        var result = await _controller.GetUserLogins(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        dynamic response = okResult.Value!;
        var returnedLogins = (UserLoginDto[])response.GetType().GetProperty("Logins")!.GetValue(response)!;
        Assert.Equal(2, returnedLogins.Length);
        Assert.Contains(returnedLogins, l => l.Provider == "Google");
        Assert.Contains(returnedLogins, l => l.Provider == "Microsoft");
    }
}