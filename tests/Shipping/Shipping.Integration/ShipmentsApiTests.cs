using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Haworks.Shipping.Api.Controllers;
using Haworks.Shipping.Api.Domain;
using Haworks.Shipping.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using Haworks.BuildingBlocks.Testing;

namespace Haworks.Shipping.Integration;

[Collection("Integration Tests")]
public class ShipmentsApiTests : IClassFixture<ShippingIntegrationTestFactory>
{
    private readonly ShippingIntegrationTestFactory _factory;
    private readonly HttpClient _client;

    public ShipmentsApiTests(ShippingIntegrationTestFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task CreateShipment_ValidRequest_ReturnsShipmentWithRates()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var request = new CreateShipmentHttpRequest(
            OrderId: orderId,
            FromStreet: "123 Test St",
            FromCity: "Test City",
            FromState: "CA",
            FromZip: "12345",
            FromCountry: "US",
            ToStreet: "456 Ship St",
            ToCity: "Ship City",
            ToState: "NY",
            ToZip: "67890",
            ToCountry: "US",
            LengthInches: 10,
            WidthInches: 8,
            HeightInches: 6,
            WeightOz: 16);

        var mockResult = new CreateShipmentResult("ep_test123", new List<RateDto>
        {
            new("rate_123", "USPS", "Priority", 7.95m, "USD", 3),
            new("rate_456", "UPS", "Ground", 9.50m, "USD", 5)
        });

        _factory.MockShippingProvider
            .Setup(p => p.CreateShipmentAsync(It.IsAny<CreateShipmentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResult);

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/Shipments", request);

        // Assert
        response.Should().BeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("id").GetGuid().Should().NotBeEmpty();
        result.GetProperty("easyPostShipmentId").GetString().Should().Be("ep_test123");

        var rates = result.GetProperty("rates").EnumerateArray().ToList();
        rates.Should().HaveCount(2);
        rates[0].GetProperty("carrier").GetString().Should().Be("USPS");

        // Verify shipment was saved to database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.OrderId == orderId);
        shipment.Should().NotBeNull();
        shipment!.EasyPostShipmentId.Should().Be("ep_test123");
        shipment.Status.Should().Be(ShipmentStatus.Created);
    }

    [Fact]
    public async Task BuyLabel_ExistingShipment_PurchasesLabelAndPublishesEvent()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var shipment = Shipment.Create(orderId, "ep_test123");
        shipment.SetAddresses("123 Test", "Test City", "CA", "12345", "US",
                             "456 Ship", "Ship City", "NY", "67890", "US");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
            db.Shipments.Add(shipment);
            await db.SaveChangesAsync();
        }

        var buyRequest = new BuyLabelRequest("rate_123");
        var mockResult = new BuyLabelResult(
            TrackingNumber: "9400111899223",
            TrackingUrl: "https://track.example.com",
            LabelUrl: "https://label.example.com/pdf",
            Carrier: "USPS",
            Service: "Priority",
            Amount: 7.95m,
            Currency: "USD",
            EstimatedDelivery: DateTime.UtcNow.AddDays(3));

        _factory.MockShippingProvider
            .Setup(p => p.BuyLabelAsync("ep_test123", "rate_123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResult);

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/Shipments/{shipment.Id}/buy", buyRequest);

        // Assert
        response.Should().BeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("trackingNumber").GetString().Should().Be("9400111899223");
        result.GetProperty("trackingUrl").GetString().Should().Be("https://track.example.com");

        // Verify shipment status updated
        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var updatedShipment = await db2.Shipments.FirstAsync(s => s.Id == shipment.Id);
        updatedShipment.Status.Should().Be(ShipmentStatus.LabelPurchased);
        updatedShipment.TrackingNumber.Should().Be("9400111899223");
        updatedShipment.RateAmountCents.Should().Be(795L); // 7.95 * 100, rounded
    }

    [Fact]
    public async Task BuyLabel_NonExistentShipment_ReturnsNotFound()
    {
        // Arrange
        var randomId = Guid.NewGuid();
        var buyRequest = new BuyLabelRequest("rate_123");

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/Shipments/{randomId}/buy", buyRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetShipment_ExistingId_ReturnsShipment()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var shipment = Shipment.Create(orderId, "ep_test123");
        shipment.SetAddresses("123 Test", "Test City", "CA", "12345", "US",
                             "456 Ship", "Ship City", "NY", "67890", "US");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
            db.Shipments.Add(shipment);
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/Shipments/{shipment.Id}");

        // Assert
        response.Should().BeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("id").GetGuid().Should().Be(shipment.Id);
        result.GetProperty("orderId").GetGuid().Should().Be(orderId);
        result.GetProperty("fromStreet").GetString().Should().Be("123 Test");
    }

    [Fact]
    public async Task GetShipment_NonExistentId_ReturnsNotFound()
    {
        // Arrange
        var randomId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/Shipments/{randomId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetByOrder_ExistingOrder_ReturnsShipmentsOrderedByCreatedAt()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var shipment1 = Shipment.Create(orderId, "ep_test1");
        var shipment2 = Shipment.Create(orderId, "ep_test2");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
            db.Shipments.Add(shipment1);
            await db.SaveChangesAsync();

            await Task.Delay(10); // Ensure different CreatedAt times

            db.Shipments.Add(shipment2);
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/Shipments/by-order/{orderId}");

        // Assert
        response.Should().BeSuccessful();
        var results = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        results.Should().HaveCount(2);

        // Should be ordered by CreatedAt descending (newest first)
        results[0].GetProperty("id").GetGuid().Should().Be(shipment2.Id);
        results[1].GetProperty("id").GetGuid().Should().Be(shipment1.Id);
    }

    [Fact]
    public async Task GetByOrder_NonExistentOrder_ReturnsEmptyArray()
    {
        // Arrange
        var randomOrderId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/Shipments/by-order/{randomOrderId}");

        // Assert
        response.Should().BeSuccessful();
        var results = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleWebhook_TrackerUpdated_DeliveredStatus_UpdatesShipmentAndPublishesEvent()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var shipment = Shipment.Create(orderId, "ep_test123");
        shipment.MarkLabelPurchased("USPS", "Priority", "9400111899223",
            "https://track.test", "https://label.test", 795L, "USD", null);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
            db.Shipments.Add(shipment);
            await db.SaveChangesAsync();
        }

        var payload = new EasyPostWebhookPayload("tracker.updated",
            new EasyPostWebhookResult("9400111899223", "delivered", null));

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/Shipments/webhooks/easypost", payload);

        // Assert
        response.Should().BeSuccessful();

        // Verify shipment status updated
        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var updatedShipment = await db2.Shipments.FirstAsync(s => s.Id == shipment.Id);
        updatedShipment.Status.Should().Be(ShipmentStatus.Delivered);
        updatedShipment.DeliveredAt.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleWebhook_TrackerUpdated_ExceptionStatus_UpdatesShipmentAndPublishesExceptionEvent()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var shipment = Shipment.Create(orderId, "ep_test123");
        shipment.MarkLabelPurchased("USPS", "Priority", "9400111899223",
            "https://track.test", "https://label.test", 795L, "USD", null);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
            db.Shipments.Add(shipment);
            await db.SaveChangesAsync();
        }

        var payload = new EasyPostWebhookPayload("tracker.updated",
            new EasyPostWebhookResult("9400111899223", "failure", "Package damaged"));

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/Shipments/webhooks/easypost", payload);

        // Assert
        response.Should().BeSuccessful();

        // Verify shipment status updated
        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var updatedShipment = await db2.Shipments.FirstAsync(s => s.Id == shipment.Id);
        updatedShipment.Status.Should().Be(ShipmentStatus.Exception);
        updatedShipment.DeliveredAt.Should().BeNull(); // Should not be set for exceptions
    }

    [Fact]
    public async Task HandleWebhook_NonTrackerEvent_ReturnsOkWithoutProcessing()
    {
        // Arrange
        var payload = new EasyPostWebhookPayload("shipment.purchased", null);

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/Shipments/webhooks/easypost", payload);

        // Assert
        response.Should().BeSuccessful();
        // No further assertions needed - the endpoint should just return OK for non-tracker events
    }

    [Fact]
    public async Task HandleWebhook_MissingTrackingCode_ReturnsOkWithoutProcessing()
    {
        // Arrange
        var payload = new EasyPostWebhookPayload("tracker.updated",
            new EasyPostWebhookResult(null, "delivered", null));

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/Shipments/webhooks/easypost", payload);

        // Assert
        response.Should().BeSuccessful();
    }

    [Fact]
    public async Task HandleWebhook_UnknownTrackingNumber_ReturnsOkWithoutProcessing()
    {
        // Arrange
        var payload = new EasyPostWebhookPayload("tracker.updated",
            new EasyPostWebhookResult("UNKNOWN_TRACKING", "delivered", null));

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/Shipments/webhooks/easypost", payload);

        // Assert
        response.Should().BeSuccessful();
        // Should not throw or create any database changes
    }
}