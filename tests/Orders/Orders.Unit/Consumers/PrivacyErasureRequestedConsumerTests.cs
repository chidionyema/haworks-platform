using FluentAssertions;
using Haworks.Contracts.Privacy;
using Haworks.Orders.Application.Commands;
using Haworks.Orders.Application.Consumers;
using Haworks.Orders.Domain;
using Haworks.Orders.Domain.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Haworks.Orders.Unit.Consumers;

public class PrivacyErasureRequestedConsumerTests
{
    private readonly Mock<IOrderRepository> _orderRepositoryMock;
    private readonly Mock<ILogger<PrivacyErasureRequestedConsumer>> _loggerMock;
    private readonly Mock<ConsumeContext<PrivacyErasureRequested>> _contextMock;
    private readonly PrivacyErasureRequestedConsumer _consumer;

    public PrivacyErasureRequestedConsumerTests()
    {
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _loggerMock = new Mock<ILogger<PrivacyErasureRequestedConsumer>>();
        _contextMock = new Mock<ConsumeContext<PrivacyErasureRequested>>();
        _consumer = new PrivacyErasureRequestedConsumer(_orderRepositoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Consume_ValidErasureRequest_AnonymisesOrdersAndPublishesCompleted()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var order1 = Order.Create("user123", 1000L, "USD", Guid.NewGuid(), "key1", "user@example.com",
            [new CreateOrderCommand.OrderItemCommand("Product1", 1, 1000L, Guid.NewGuid())]);

        var order2 = Order.Create("user123", 2000L, "USD", Guid.NewGuid(), "key2", "user@example.com",
            [new CreateOrderCommand.OrderItemCommand("Product2", 2, 1000L, Guid.NewGuid())]);

        var erasureEvent = new PrivacyErasureRequested
        {
            RequestId = requestId,
            UserId = userId,
            RequestedAt = DateTime.UtcNow
        };

        _contextMock.Setup(c => c.Message).Returns(erasureEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Mock paginated results - return orders on first call, empty on second
        _orderRepositoryMock.SetupSequence(r => r.ListByUserTrackedAsync(
                userId.ToString(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Order> { order1, order2 })  // First page
            .ReturnsAsync(new List<Order>());                   // Second page (empty)

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        order1.UserId.Should().Be("ERASED");
        order1.CustomerEmail.Should().Be("deleted@privacy.invalid");
        order2.UserId.Should().Be("ERASED");
        order2.CustomerEmail.Should().Be("deleted@privacy.invalid");

        // Verify completion event was published
        _contextMock.Verify(c => c.Publish(
            It.Is<PrivacyErasureCompleted>(e =>
                e.RequestId == requestId &&
                e.UserId == userId &&
                e.ServiceName == "orders-svc"),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify no failure event was published
        _contextMock.Verify(c => c.Publish(It.IsAny<PrivacyErasureFailed>(), It.IsAny<CancellationToken>()), Times.Never);

        // Verify logs
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"GDPR erasure requested for UserId={userId}")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Anonymised 2 orders")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_NoOrdersFound_PublishesCompletedWithZeroCount()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var erasureEvent = new PrivacyErasureRequested
        {
            RequestId = requestId,
            UserId = userId,
            RequestedAt = DateTime.UtcNow
        };

        _contextMock.Setup(c => c.Message).Returns(erasureEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Mock no orders found
        _orderRepositoryMock.Setup(r => r.ListByUserTrackedAsync(
                userId.ToString(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Order>());

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        // Verify completion event was published
        _contextMock.Verify(c => c.Publish(
            It.Is<PrivacyErasureCompleted>(e =>
                e.RequestId == requestId &&
                e.UserId == userId &&
                e.ServiceName == "orders-svc"),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify logs show 0 anonymised
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Anonymised 0 orders")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_PaginationHandlesLargeBatch_ProcessesAllOrders()
    {
        // Arrange - simulate 300 orders across 2 pages (200 + 100)
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var firstBatch = Enumerable.Range(1, 200)
            .Select(i => Order.Create($"user{i}", 1000L, "USD", Guid.NewGuid(), $"key{i}", "user@example.com",
                [new CreateOrderCommand.OrderItemCommand($"Product{i}", 1, 1000L, Guid.NewGuid())]))
            .ToList();

        var secondBatch = Enumerable.Range(201, 100)
            .Select(i => Order.Create($"user{i}", 1000L, "USD", Guid.NewGuid(), $"key{i}", "user@example.com",
                [new CreateOrderCommand.OrderItemCommand($"Product{i}", 1, 1000L, Guid.NewGuid())]))
            .ToList();

        var erasureEvent = new PrivacyErasureRequested
        {
            RequestId = requestId,
            UserId = userId,
            RequestedAt = DateTime.UtcNow
        };

        _contextMock.Setup(c => c.Message).Returns(erasureEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Mock paginated results
        _orderRepositoryMock.SetupSequence(r => r.ListByUserTrackedAsync(
                userId.ToString(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstBatch)   // First page: skip=0, take=200
            .ReturnsAsync(secondBatch)  // Second page: skip=200, take=200
            .ReturnsAsync(new List<Order>());  // Third page: empty

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        // Verify all orders were anonymised
        firstBatch.Should().AllSatisfy(o =>
        {
            o.UserId.Should().Be("ERASED");
            o.CustomerEmail.Should().Be("deleted@privacy.invalid");
        });

        secondBatch.Should().AllSatisfy(o =>
        {
            o.UserId.Should().Be("ERASED");
            o.CustomerEmail.Should().Be("deleted@privacy.invalid");
        });

        // Verify completion event was published
        _contextMock.Verify(c => c.Publish(
            It.Is<PrivacyErasureCompleted>(e =>
                e.RequestId == requestId &&
                e.UserId == userId &&
                e.ServiceName == "orders-svc"),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify logs show correct total count
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Anonymised 300 orders")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Verify repository was called with correct pagination parameters
        _orderRepositoryMock.Verify(r => r.ListByUserTrackedAsync(userId.ToString(), 0, 200, It.IsAny<CancellationToken>()), Times.Once);
        _orderRepositoryMock.Verify(r => r.ListByUserTrackedAsync(userId.ToString(), 200, 200, It.IsAny<CancellationToken>()), Times.Once);
        _orderRepositoryMock.Verify(r => r.ListByUserTrackedAsync(userId.ToString(), 400, 200, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_ExceptionOccurs_PublishesFailureEvent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var erasureEvent = new PrivacyErasureRequested
        {
            RequestId = requestId,
            UserId = userId,
            RequestedAt = DateTime.UtcNow
        };

        _contextMock.Setup(c => c.Message).Returns(erasureEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Mock repository to throw exception
        var expectedException = new InvalidOperationException("Database connection failed");
        _orderRepositoryMock.Setup(r => r.ListByUserTrackedAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        // Verify failure event was published
        _contextMock.Verify(c => c.Publish(
            It.Is<PrivacyErasureFailed>(e =>
                e.RequestId == requestId &&
                e.UserId == userId &&
                e.ServiceName == "orders-svc" &&
                e.ErrorMessage == expectedException.Message),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify no completion event was published
        _contextMock.Verify(c => c.Publish(It.IsAny<PrivacyErasureCompleted>(), It.IsAny<CancellationToken>()), Times.Never);

        // Verify error was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Terminal failure during GDPR erasure")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_OperationCancelledException_DoesNotPublishFailureEvent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var erasureEvent = new PrivacyErasureRequested
        {
            RequestId = requestId,
            UserId = userId,
            RequestedAt = DateTime.UtcNow
        };

        var cancellationToken = new CancellationToken(true); // Already cancelled
        _contextMock.Setup(c => c.Message).Returns(erasureEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(cancellationToken);

        // Mock repository to throw OperationCanceledException
        _orderRepositoryMock.Setup(r => r.ListByUserTrackedAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert - should propagate OperationCanceledException
        await Assert.ThrowsAsync<OperationCanceledException>(() => _consumer.Consume(_contextMock.Object));

        // Verify no failure or completion events were published
        _contextMock.Verify(c => c.Publish(It.IsAny<PrivacyErasureFailed>(), It.IsAny<CancellationToken>()), Times.Never);
        _contextMock.Verify(c => c.Publish(It.IsAny<PrivacyErasureCompleted>(), It.IsAny<CancellationToken>()), Times.Never);

        // Verify no error was logged (OperationCanceledException is not caught)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}