using FluentAssertions;
using Haworks.Contracts.Orders;
using Haworks.Realtime.Api.Application.Common;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Haworks.BuildingBlocks.Testing;

namespace Haworks.Realtime.Integration;

[Collection("Realtime Integration")]
public class OrderStatusChangedConsumerTests : IAsyncLifetime
{
    private readonly RealtimeWebAppFactory _factory;
    private readonly Guid _testCustomerId = Guid.NewGuid();
    private readonly Guid _testOrderId = Guid.NewGuid();

    public OrderStatusChangedConsumerTests(RealtimeWebAppFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync() => _factory.ResetRedisAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Consume_OrderStatusChanged_Should_TriggerNotification()
    {
        // Arrange
        var testBus = _factory.Services.GetRequiredService<ITestHarness>();
        var inboxService = _factory.Services.GetRequiredService<IInboxService>();

        var orderEvent = new OrderStatusChanged
        {
            OrderId = _testOrderId,
            CustomerId = _testCustomerId,
            NewStatus = "Shipped",
            ChangedAt = DateTime.UtcNow
        };

        // Verify inbox is initially empty
        var initialMessages = await inboxService.GetMessagesAsync(_testCustomerId);
        initialMessages.Should().BeEmpty();

        // Act - Publish the event to the test harness
        await testBus.Bus.Publish(orderEvent);

        // Assert - Wait for the consumer to process the event and create a notification
        await TestWait.Until(async () =>
        {
            var messages = await inboxService.GetMessagesAsync(_testCustomerId);
            return messages.Count > 0;
        }, TimeSpan.FromSeconds(10));

        // Verify the notification was created in the inbox
        var inboxMessages = await inboxService.GetMessagesAsync(_testCustomerId);
        inboxMessages.Should().HaveCount(1);

        var notification = inboxMessages[0];
        notification.MessageType.Should().Be("OrderStatusChanged");
        notification.Data.Should().NotBeNull();

        // Verify the notification data contains the expected order information
        notification.Data.Should().NotBeNull();

        // Verify the data contains the order information as JSON
        var dataString = notification.Data.ToString();
        dataString.Should().Contain(_testOrderId.ToString());
        dataString.Should().Contain("Shipped");
    }

    [Fact]
    public async Task Consume_OrderStatusChanged_Should_CreateNotificationWithCorrectData()
    {
        // Arrange
        var testBus = _factory.Services.GetRequiredService<ITestHarness>();
        var inboxService = _factory.Services.GetRequiredService<IInboxService>();

        var expectedStatus = "Processing";
        var expectedChangedAt = DateTime.UtcNow;

        var orderEvent = new OrderStatusChanged
        {
            OrderId = _testOrderId,
            CustomerId = _testCustomerId,
            NewStatus = expectedStatus,
            ChangedAt = expectedChangedAt
        };

        // Act
        await testBus.Bus.Publish(orderEvent);

        // Wait for processing
        await TestWait.Until(async () =>
        {
            var messages = await inboxService.GetMessagesAsync(_testCustomerId);
            return messages.Count > 0;
        }, TimeSpan.FromSeconds(10));

        // Assert
        var inboxMessages = await inboxService.GetMessagesAsync(_testCustomerId);
        var notification = inboxMessages[0];

        notification.MessageType.Should().Be("OrderStatusChanged");
        notification.MessageId.Should().NotBeEmpty();

        // The Data should be a JSON object containing OrderId, NewStatus, and ChangedAt
        // Since it comes back as JsonElement, we need to check it differently
        var dataString = notification.Data.ToString();
        dataString.Should().Contain(_testOrderId.ToString());
        dataString.Should().Contain(expectedStatus);
    }

    [Fact]
    public async Task Consume_MultipleOrderEvents_Should_CreateMultipleNotifications()
    {
        // Arrange
        var testBus = _factory.Services.GetRequiredService<ITestHarness>();
        var inboxService = _factory.Services.GetRequiredService<IInboxService>();

        var order1Event = new OrderStatusChanged
        {
            OrderId = Guid.NewGuid(),
            CustomerId = _testCustomerId,
            NewStatus = "Shipped",
            ChangedAt = DateTime.UtcNow
        };

        var order2Event = new OrderStatusChanged
        {
            OrderId = Guid.NewGuid(),
            CustomerId = _testCustomerId,
            NewStatus = "Delivered",
            ChangedAt = DateTime.UtcNow.AddMinutes(5)
        };

        // Act - Publish multiple events
        await testBus.Bus.Publish(order1Event);
        await testBus.Bus.Publish(order2Event);

        // Assert - Wait for both notifications to be created
        await TestWait.Until(async () =>
        {
            var messages = await inboxService.GetMessagesAsync(_testCustomerId);
            return messages.Count >= 2;
        }, TimeSpan.FromSeconds(15));

        var inboxMessages = await inboxService.GetMessagesAsync(_testCustomerId);
        inboxMessages.Should().HaveCount(2);

        // Verify both notifications have the correct type
        inboxMessages.Should().AllSatisfy(msg => msg.MessageType.Should().Be("OrderStatusChanged"));

        // Verify they have different message IDs (deduplication working correctly)
        var messageIds = inboxMessages.Select(m => m.MessageId).ToList();
        messageIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Consume_OrderEventForDifferentCustomer_Should_NotAffectTargetCustomerInbox()
    {
        // Arrange
        var testBus = _factory.Services.GetRequiredService<ITestHarness>();
        var inboxService = _factory.Services.GetRequiredService<IInboxService>();
        var otherCustomerId = Guid.NewGuid();

        var orderEventForOtherCustomer = new OrderStatusChanged
        {
            OrderId = Guid.NewGuid(),
            CustomerId = otherCustomerId,
            NewStatus = "Shipped",
            ChangedAt = DateTime.UtcNow
        };

        // Act - Publish event for a different customer
        await testBus.Bus.Publish(orderEventForOtherCustomer);

        // Wait a bit for processing
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert - Our test customer's inbox should remain empty
        var testCustomerMessages = await inboxService.GetMessagesAsync(_testCustomerId);
        testCustomerMessages.Should().BeEmpty();

        // But the other customer should have the notification
        await TestWait.Until(async () =>
        {
            var messages = await inboxService.GetMessagesAsync(otherCustomerId);
            return messages.Count > 0;
        }, TimeSpan.FromSeconds(10));

        var otherCustomerMessages = await inboxService.GetMessagesAsync(otherCustomerId);
        otherCustomerMessages.Should().HaveCount(1);
    }
}