using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.CheckoutOrchestrator.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haworks.CheckoutOrchestrator.Integration;

[Collection(nameof(CheckoutRealTransportCollection))]
public sealed class GetEndpointsTests : IAsyncLifetime
{
    private readonly CheckoutWebAppFactory _factory;
    private CheckoutSagaState _testSaga = null!;

    public GetEndpointsTests(CheckoutWebAppFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureSchemaAsync();
        await _factory.ResetDatabaseAsync();

        // Create a test saga for GET endpoint tests
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckoutDbContext>();

        _testSaga = new CheckoutSagaState
        {
            CorrelationId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "test-user-123",
            CustomerEmail = "test@example.com",
            TotalAmountCents = 5000L,
            Currency = "USD",
            CurrentState = "Initiated",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.CheckoutSagas.Add(_testSaga);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Get_WithValidSagaId_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = JwtTestDefaults.CreateAdminAuthHeader();

        // Act
        var response = await client.GetAsync($"/api/v1/checkouts/{_testSaga.CorrelationId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain(_testSaga.CorrelationId.ToString());
        content.Should().Contain(_testSaga.OrderId.ToString());
        content.Should().Contain("test@example.com");
    }

    [Fact]
    public async Task Get_WithNonExistentSagaId_ReturnsNotFound()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = JwtTestDefaults.CreateAdminAuthHeader();
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/api/v1/checkouts/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_WithoutAuthorization_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/v1/checkouts/{_testSaga.CorrelationId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetByOrderId_WithValidOrderId_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = JwtTestDefaults.CreateAdminAuthHeader();

        // Act
        var response = await client.GetAsync($"/api/v1/checkouts/by-order/{_testSaga.OrderId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain(_testSaga.CorrelationId.ToString());
        content.Should().Contain(_testSaga.OrderId.ToString());
    }

    [Fact]
    public async Task GetByOrderId_WithNonExistentOrderId_ReturnsNotFound()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = JwtTestDefaults.CreateAdminAuthHeader();
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/api/v1/checkouts/by-order/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_WithAdminRole_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = JwtTestDefaults.CreateAdminAuthHeader();

        // Act
        var response = await client.GetAsync("/api/v1/checkouts");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain(_testSaga.CorrelationId.ToString());
    }

    [Fact]
    public async Task List_WithStateFilter_ReturnsFiltered()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = JwtTestDefaults.CreateAdminAuthHeader();

        // Act
        var response = await client.GetAsync("/api/v1/checkouts?state=Initiated");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain(_testSaga.CorrelationId.ToString());
    }

    [Fact]
    public async Task List_WithNonMatchingStateFilter_ReturnsEmpty()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = JwtTestDefaults.CreateAdminAuthHeader();

        // Act
        var response = await client.GetAsync("/api/v1/checkouts?state=Completed");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotContain(_testSaga.CorrelationId.ToString());
    }

    [Fact]
    public async Task List_WithLimitAndOffset_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = JwtTestDefaults.CreateAdminAuthHeader();

        // Act
        var response = await client.GetAsync("/api/v1/checkouts?limit=10&offset=0");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAudit_WithValidSagaId_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = JwtTestDefaults.CreateAdminAuthHeader();

        // Act
        var response = await client.GetAsync($"/api/v1/checkouts/{_testSaga.CorrelationId}/audit");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAudit_WithNonExistentSagaId_ReturnsNotFound()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = JwtTestDefaults.CreateAdminAuthHeader();
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/api/v1/checkouts/{nonExistentId}/audit");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StartCheckout_WithInvalidCurrency_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = JwtTestDefaults.CreateAdminAuthHeader();

        var request = new
        {
            SagaId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "test-user",
            CustomerEmail = "test@example.com",
            TotalAmount = 99.99m, // This will cause Money.TryFromMajorUnits to fail with invalid conversion
            IdempotencyKey = "test-key",
            Items = new[]
            {
                new
                {
                    ProductId = Guid.NewGuid(),
                    ProductName = "Test Product",
                    Quantity = 1,
                    UnitPriceCents = 9999L,
                    Currency = "INVALID_CURRENCY_CODE_TOO_LONG"
                }
            },
            Currency = "INVALID_CURRENCY_CODE_TOO_LONG"
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/api/v1/checkouts", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}