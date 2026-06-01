using Haworks.Identity.Api.Controllers;
using Haworks.Identity.Api.Models;
using Haworks.Identity.Application;
using Haworks.Identity.Application.DTOs;
using Haworks.BuildingBlocks.Common;
using Haworks.BuildingBlocks.Testing;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Antiforgery;
using Moq;
using Xunit.Abstractions;
using Xunit;

namespace Haworks.Identity.UnitTests.Controllers;

public class AuthenticationControllerTests : TestBase
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<IAntiforgery> _antiforgeryMock;
    private readonly AuthenticationController _controller;

    public AuthenticationControllerTests(ITestOutputHelper output) : base(output)
    {
        _mediatorMock = new Mock<IMediator>();
        _antiforgeryMock = new Mock<IAntiforgery>();
        _controller = new AuthenticationController(_mediatorMock.Object, _antiforgeryMock.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsOk()
    {
        var request = new LoginRequest("user", "pass");
        var authResponseDto = new AuthResponseDto
        {
            Token = "token",
            RefreshToken = "refresh",
            Expires = DateTime.UtcNow.AddMinutes(15),
            Username = "user",
            Email = "email",
            UserId = "id",
            Message = "Success"
        };

        _mediatorMock
            .Setup(m => m.Send(It.Is<LoginCommand>(c =>
                c.Username == request.Username &&
                c.Password == request.Password), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(authResponseDto));

        var result = await _controller.Login(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AuthResponse>(okResult.Value);
        Assert.Equal("token", response.Token);
        Assert.Equal("refresh", response.RefreshToken);
        Assert.Equal("user", response.Username);
        Assert.Equal("email", response.Email);
        Assert.Equal("id", response.UserId);
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsBadRequest()
    {
        var request = new LoginRequest("user", "wrong");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AuthResponseDto>(
                Error.Validation("Auth.InvalidCredentials", "Invalid username or password")));

        var result = await _controller.Login(request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
    }

    [Fact]
    public async Task Register_WithValidData_ReturnsCreated()
    {
        var request = new RegisterRequest("newuser", "new@email.com", "StrongPass1!");
        var authResponseDto = new AuthResponseDto
        {
            Token = "token",
            RefreshToken = "refresh",
            Expires = DateTime.UtcNow.AddMinutes(15),
            Username = "newuser",
            Email = "new@email.com",
            UserId = "new-id",
            Message = "Registered"
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RegisterCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(authResponseDto));

        var result = await _controller.Register(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<AuthResponse>(createdResult.Value);
        Assert.Equal("token", response.Token);
        Assert.Equal("refresh", response.RefreshToken);
        Assert.Equal("newuser", response.Username);
        Assert.Equal("new@email.com", response.Email);
        Assert.Equal("new-id", response.UserId);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsBadRequest()
    {
        var request = new RegisterRequest("newuser", "existing@email.com", "StrongPass1!");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RegisterCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AuthResponseDto>(
                Error.Validation("Auth.DuplicateEmail", "Email already registered")));

        var result = await _controller.Register(request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
    }

    [Fact]
    public async Task Logout_WithValidRequest_ReturnsOk()
    {
        var request = new LogoutRequest("valid-token");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<LogoutCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success("Successfully logged out"));

        var result = await _controller.Logout(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        dynamic response = okResult.Value!;
        Assert.Equal("Successfully logged out", response.GetType().GetProperty("message")!.GetValue(response));
    }

    [Fact]
    public async Task Logout_WithInvalidToken_ReturnsBadRequest()
    {
        var request = new LogoutRequest("invalid-token");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<LogoutCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<string>(
                Error.Validation("Auth.InvalidToken", "Invalid or expired token")));

        var result = await _controller.Logout(request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
    }

    [Fact]
    public async Task VerifyToken_WithValidToken_ReturnsOk()
    {
        var query = new VerifyTokenQuery("valid-token");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<VerifyTokenQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new VerifyTokenResult
            {
                IsValid = true,
                Username = "testuser",
                Email = "test@example.com",
                UserId = "user-123",
                Roles = ["User"]
            }));

        var result = await _controller.VerifyToken(query, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<VerifyTokenResult>(okResult.Value);
        Assert.True(response.IsValid);
        Assert.Equal("testuser", response.Username);
        Assert.Equal("test@example.com", response.Email);
        Assert.Equal("user-123", response.UserId);
    }

    [Fact]
    public async Task VerifyToken_WithInvalidToken_ReturnsBadRequest()
    {
        var query = new VerifyTokenQuery("invalid-token");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<VerifyTokenQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<VerifyTokenResult>(
                Error.Validation("Auth.InvalidToken", "Token is invalid or expired")));

        var result = await _controller.VerifyToken(query, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
    }

    [Fact]
    public async Task ServiceToken_WithValidRequest_ReturnsOk()
    {
        var request = new ServiceTokenRequest("valid-service-secret");
        var serviceTokenDto = new ServiceTokenDto
        {
            Token = "service-token",
            Expires = DateTime.UtcNow.AddHours(1),
            ServiceName = "test-service"
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateServiceTokenCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(serviceTokenDto));

        var result = await _controller.ServiceToken(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ServiceTokenResponse>(okResult.Value);
        Assert.Equal("service-token", response.Token);
        Assert.Equal(serviceTokenDto.Expires, response.Expires);
        Assert.Equal("test-service", response.ServiceName);
    }

    [Fact]
    public async Task ServiceToken_WithInvalidSecret_ReturnsUnauthorized()
    {
        var request = new ServiceTokenRequest("invalid-secret");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateServiceTokenCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ServiceTokenDto>(
                Error.Authentication("Auth.InvalidServiceSecret", "Invalid service secret")));

        var result = await _controller.ServiceToken(request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(401, objectResult.StatusCode);
    }
}
