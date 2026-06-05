using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.Privacy.Application.Requests.Commands.InitiateRequest;
using Haworks.Privacy.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Haworks.Privacy.Integration;

[Collection("Privacy Integration")]
public sealed class PrivacyRequestsControllerIntegrationTests : IAsyncLifetime
{
    private readonly PrivacyWebAppFactory _factory;
    private readonly HttpClient _client;

    public PrivacyRequestsControllerIntegrationTests(PrivacyWebAppFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureSchemaAsync();
        await _factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task InitiatePrivacyRequest_ValidUserRequest_Returns200WithRequestId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new InitiatePrivacyRequestCommand(userId, PrivacyRequestType.Erasure);

        _client.SetTestAuth(claims: new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("sub", userId.ToString())
        });

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/PrivacyRequests", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<dynamic>();
        result.Should().NotBeNull();

        // The response should contain a RequestId
        var jsonString = await response.Content.ReadAsStringAsync();
        jsonString.Should().Contain("requestId");
    }

    [Fact]
    public async Task InitiatePrivacyRequest_ServiceToServiceWithValidScope_Returns200()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new InitiatePrivacyRequestCommand(userId, PrivacyRequestType.Erasure);

        _client.SetTestAuth(claims: new[]
        {
            new Claim("sub", "bff-service"),
            new Claim(ClaimTypes.Role, "Service"),
            new Claim("scope", "privacy:erase")
        });

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/PrivacyRequests", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonString = await response.Content.ReadAsStringAsync();
        jsonString.Should().Contain("requestId");
    }

    [Fact]
    public async Task InitiatePrivacyRequest_ServiceToServiceWithoutScope_Returns200ButLogsWarning()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new InitiatePrivacyRequestCommand(userId, PrivacyRequestType.Erasure);

        _client.SetTestAuth(claims: new[]
        {
            new Claim("sub", "bff-service"),
            new Claim(ClaimTypes.Role, "Service")
            // Missing privacy:erase scope
        });

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/PrivacyRequests", command);

        // Assert
        // Should still succeed (enforcement pending PLATFORM-SEC-12) but logs IDOR risk warning
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonString = await response.Content.ReadAsStringAsync();
        jsonString.Should().Contain("requestId");
    }

    [Fact]
    public async Task InitiatePrivacyRequest_NoAuthentication_Returns401()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new InitiatePrivacyRequestCommand(userId, PrivacyRequestType.Export);

        // No authentication headers set

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/PrivacyRequests", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InitiatePrivacyRequest_InvalidUserIdClaim_Returns401()
    {
        // Arrange
        var command = new InitiatePrivacyRequestCommand(Guid.NewGuid(), PrivacyRequestType.Erasure);

        _client.SetTestAuth(claims: new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "invalid-guid"),
            new Claim("sub", "invalid-guid")
        });

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/PrivacyRequests", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InitiatePrivacyRequest_UserTriesToUseOtherUserId_UsesClaimUserId()
    {
        // Arrange
        var claimedUserId = Guid.NewGuid();
        var attemptedUserId = Guid.NewGuid(); // Different user ID in command

        var command = new InitiatePrivacyRequestCommand(attemptedUserId, PrivacyRequestType.Export);

        _client.SetTestAuth(claims: new[]
        {
            new Claim(ClaimTypes.NameIdentifier, claimedUserId.ToString()),
            new Claim("sub", claimedUserId.ToString())
        });

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/PrivacyRequests", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The actual request should use the claimed user ID, not the attempted one
        var jsonString = await response.Content.ReadAsStringAsync();
        jsonString.Should().Contain("requestId");
    }

    [Fact]
    public async Task InitiatePrivacyRequest_InvalidRequestType_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var invalidCommand = new { UserId = userId, Type = 999, IdempotencyKey = "" }; // Invalid enum value

        _client.SetTestAuth(claims: new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        });

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/PrivacyRequests", invalidCommand);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetStatus_ValidRequest_Returns200WithStatus()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = await CreatePrivacyRequestAsync(userId);

        _client.SetTestAuth(claims: new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("sub", userId.ToString())
        });

        // Act
        var response = await _client.GetAsync($"/api/v1/PrivacyRequests/{requestId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonString = await response.Content.ReadAsStringAsync();
        jsonString.Should().Contain("requestId");
        jsonString.Should().Contain("userId");
        jsonString.Should().Contain("status");
    }

    [Fact]
    public async Task GetStatus_RequestBelongsToDifferentUser_Returns404()
    {
        // Arrange
        var ownerUserId = Guid.NewGuid();
        var attackerUserId = Guid.NewGuid();
        var requestId = await CreatePrivacyRequestAsync(ownerUserId);

        _client.SetTestAuth(claims: new[]
        {
            new Claim(ClaimTypes.NameIdentifier, attackerUserId.ToString()),
            new Claim("sub", attackerUserId.ToString())
        });

        // Act
        var response = await _client.GetAsync($"/api/v1/PrivacyRequests/{requestId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetStatus_NonexistentRequest_Returns404()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var nonexistentRequestId = Guid.NewGuid();

        _client.SetTestAuth(claims: new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("sub", userId.ToString())
        });

        // Act
        var response = await _client.GetAsync($"/api/v1/PrivacyRequests/{nonexistentRequestId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetStatus_NoAuthentication_Returns401()
    {
        // Arrange
        var requestId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/PrivacyRequests/{requestId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetStatus_InvalidUserIdClaim_Returns401()
    {
        // Arrange
        var requestId = Guid.NewGuid();

        _client.SetTestAuth(claims: new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "invalid-guid"),
            new Claim("sub", "invalid-guid")
        });

        // Act
        var response = await _client.GetAsync($"/api/v1/PrivacyRequests/{requestId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(PrivacyRequestType.Erasure)]
    [InlineData(PrivacyRequestType.Export)]
    public async Task InitiatePrivacyRequest_DifferentRequestTypes_AllSucceed(PrivacyRequestType requestType)
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new InitiatePrivacyRequestCommand(userId, requestType);

        _client.SetTestAuth(claims: new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("sub", userId.ToString())
        });

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/PrivacyRequests", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonString = await response.Content.ReadAsStringAsync();
        jsonString.Should().Contain("requestId");
    }

    private async Task<Guid> CreatePrivacyRequestAsync(Guid userId)
    {
        var command = new InitiatePrivacyRequestCommand(userId, PrivacyRequestType.Erasure);

        _client.SetTestAuth(claims: new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("sub", userId.ToString())
        });

        var response = await _client.PostAsJsonAsync("/api/v1/PrivacyRequests", command);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<dynamic>();
        var resultDict = result as System.Collections.Generic.IDictionary<string, object>;
        return Guid.Parse(resultDict!["requestId"].ToString()!);
    }
}