using Haworks.Contracts.Orders;
using Haworks.Webhooks.Domain;
using Haworks.Webhooks.Application.Interfaces;
using Haworks.Webhooks.Infrastructure.Messaging;
using Haworks.Webhooks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using MassTransit;

namespace Haworks.Webhooks.Unit.Infrastructure;

public class EventFanOutConsumerTests
{
    private readonly WebhooksDbContext _db;
    private readonly Mock<IWebhookDispatcher> _mockDispatcher = new();
    private readonly Mock<ILogger<EventFanOutConsumer>> _mockLogger = new();

    public EventFanOutConsumerTests()
    {
        var options = new DbContextOptionsBuilder<WebhooksDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new WebhooksDbContext(options);
    }

    [Fact]
    public async Task Consumer_Should_Forward_Event_To_Svix_Via_Dispatcher()
    {
        // Arrange
        var partnerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var message = new OrderCreatedEvent
        {
            OrderId = orderId,
            CustomerId = Guid.NewGuid(),
            TotalAmount = 100,
            CustomerEmail = "test@example.com"
        };

        var mockContext = new Mock<ConsumeContext<OrderCreatedEvent>>();
        mockContext.Setup(x => x.Message).Returns(message);
        mockContext.Setup(x => x.MessageId).Returns(messageId);
        mockContext.Setup(x => x.CancellationToken).Returns(CancellationToken.None);

        // Setup subscription
        var sub = new WebhookSubscription(partnerId, "https://test.com", "s", "sh", "p", ["order.created"]);
        _db.Subscriptions.Add(sub);
        await _db.SaveChangesAsync();

        var consumer = new EventFanOutConsumer(_db, _mockDispatcher.Object, _mockLogger.Object);

        // Act
        await consumer.Consume(mockContext.Object);

        // Assert — dispatcher was called with the partner's ID, event type, and the message ID as event ID
        _mockDispatcher.Verify(x => x.ForwardAsync(
            partnerId,
            "order.created",
            It.Is<string>(p => p.Contains(orderId.ToString(), StringComparison.Ordinal)),
            messageId.ToString(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consumer_Should_Not_Forward_When_No_Subscriptions_Match()
    {
        // Arrange
        var message = new OrderCreatedEvent
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            TotalAmount = 50,
            CustomerEmail = "test@example.com"
        };

        var mockContext = new Mock<ConsumeContext<OrderCreatedEvent>>();
        mockContext.Setup(x => x.Message).Returns(message);
        mockContext.Setup(x => x.MessageId).Returns(Guid.NewGuid());
        mockContext.Setup(x => x.CancellationToken).Returns(CancellationToken.None);

        // No subscriptions in DB
        var consumer = new EventFanOutConsumer(_db, _mockDispatcher.Object, _mockLogger.Object);

        // Act
        await consumer.Consume(mockContext.Object);

        // Assert — dispatcher should never be called
        _mockDispatcher.Verify(
            x => x.ForwardAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consumer_Should_Forward_Once_Per_Partner_When_Multiple_Subscriptions()
    {
        // Arrange
        var partnerId = Guid.NewGuid();
        var message = new OrderCreatedEvent
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            TotalAmount = 200,
            CustomerEmail = "test@example.com"
        };

        var mockContext = new Mock<ConsumeContext<OrderCreatedEvent>>();
        mockContext.Setup(x => x.Message).Returns(message);
        mockContext.Setup(x => x.MessageId).Returns(Guid.NewGuid());
        mockContext.Setup(x => x.CancellationToken).Returns(CancellationToken.None);

        // Two subscriptions for the same partner
        _db.Subscriptions.Add(new WebhookSubscription(partnerId, "https://a.com", "s", "sh", "p", ["order.created"]));
        _db.Subscriptions.Add(new WebhookSubscription(partnerId, "https://b.com", "s", "sh", "p", ["order.created"]));
        await _db.SaveChangesAsync();

        var consumer = new EventFanOutConsumer(_db, _mockDispatcher.Object, _mockLogger.Object);

        // Act
        await consumer.Consume(mockContext.Object);

        // Assert — one call per unique partner, not per subscription
        _mockDispatcher.Verify(
            x => x.ForwardAsync(partnerId, "order.created", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
