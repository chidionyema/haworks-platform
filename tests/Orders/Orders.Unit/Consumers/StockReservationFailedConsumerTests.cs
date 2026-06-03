using FluentAssertions;
using Haworks.Contracts.Catalog;
using Haworks.Contracts.Orders;
using Haworks.Orders.Application.Commands;
using Haworks.Orders.Application.Consumers;
using Haworks.Orders.Domain;
using Haworks.Orders.Domain.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Haworks.Orders.Unit.Consumers;

public class StockReservationFailedConsumerTests
{
    private readonly Mock<IOrderRepository> _orderRepositoryMock;
    private readonly Mock<ILogger<StockReservationFailedConsumer>> _loggerMock;
    private readonly Mock<ConsumeContext<StockReservationFailedEvent>> _contextMock;
    private readonly StockReservationFailedConsumer _consumer;

    public StockReservationFailedConsumerTests()
    {
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _loggerMock = new Mock<ILogger<StockReservationFailedConsumer>>();
        _contextMock = new Mock<ConsumeContext<StockReservationFailedEvent>>();
        _consumer = new StockReservationFailedConsumer(_orderRepositoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Consume_ValidStockReservationFailed_MarksOrderAbandonedAndPublishesEvent()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var order = Order.Create("user123", 2500L, "USD", sagaId, "key", "test@example.com",
            [new CreateOrderCommand.OrderItemCommand("Limited Widget", 5, 500L, productId)]);

        var stockFailureEvent = new StockReservationFailedEvent
        {
            OrderId = orderId,
            SagaId = sagaId,
            Reason = "Insufficient stock: requested 5, available 2",
            Items = new[]
            {
                new StockReservationItem
                {
                    ProductId = productId,
                    ProductName = "Limited Widget",
                    Quantity = 5,
                    RemainingStock = 2
                }
            }
        };

        _contextMock.Setup(c => c.Message).Returns(stockFailureEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _orderRepositoryMock.Setup(r => r.GetByIdTrackedAsync(orderId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(order);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        order.Status.Should().Be(OrderStatus.Abandoned);
        order.AbandonReason.Should().Be("StockReservationFailed: Insufficient stock: requested 5, available 2");

        _contextMock.Verify(c => c.Publish(
            It.Is<OrderAbandonedEvent>(e =>
                e.OrderId == order.Id &&
                e.SagaId == sagaId &&
                e.CustomerEmail == "test@example.com" &&
                e.PreviousStatus == "Created" &&
                e.Items.Count == 1 &&
                e.Items[0].ProductId == productId &&
                e.Items[0].ProductName == "Limited Widget" &&
                e.Items[0].Quantity == 5 &&
                e.Items[0].RemainingStock == null), // No stock to release as reservation failed
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_OrderNotFound_LogsWarningAndDoesNotThrow()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var stockFailureEvent = new StockReservationFailedEvent
        {
            OrderId = orderId,
            SagaId = Guid.NewGuid(),
            Reason = "Product discontinued",
            Items = []
        };

        _contextMock.Setup(c => c.Message).Returns(stockFailureEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _orderRepositoryMock.Setup(r => r.GetByIdTrackedAsync(orderId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync((Order?)null);

        // Act & Assert - should not throw
        await _consumer.Consume(_contextMock.Object);

        // Verify warning was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Order {orderId} not found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Verify no publish occurred
        _contextMock.Verify(c => c.Publish(It.IsAny<OrderAbandonedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_OrderAlreadyInTerminalState_SkipsPublishAndLogs()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        var order = Order.Create("user123", 1000L, "USD", sagaId, "key", "test@example.com",
            [new CreateOrderCommand.OrderItemCommand("Product", 1, 1000L, Guid.NewGuid())]);

        // Mark order as already paid (terminal state)
        order.MarkPaid(Guid.NewGuid());

        var stockFailureEvent = new StockReservationFailedEvent
        {
            OrderId = orderId,
            SagaId = sagaId,
            Reason = "Too late - already processed",
            Items = []
        };

        _contextMock.Setup(c => c.Message).Returns(stockFailureEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _orderRepositoryMock.Setup(r => r.GetByIdTrackedAsync(orderId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(order);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        order.Status.Should().Be(OrderStatus.Paid); // Should remain unchanged

        // Verify informational log was written
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("already in terminal status")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Verify no publish occurred (idempotent behavior)
        _contextMock.Verify(c => c.Publish(It.IsAny<OrderAbandonedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Insufficient stock: requested 10, available 3")]
    [InlineData("Product discontinued")]
    [InlineData("Inventory locked for maintenance")]
    [InlineData("Pricing error - product removed from catalog")]
    public async Task Consume_DifferentReasons_FormatsAbandonReasonCorrectly(string reason)
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        var order = Order.Create("user123", 1000L, "USD", sagaId, "key", "test@example.com",
            [new CreateOrderCommand.OrderItemCommand("Product", 1, 1000L, Guid.NewGuid())]);

        var stockFailureEvent = new StockReservationFailedEvent
        {
            OrderId = orderId,
            SagaId = sagaId,
            Reason = reason,
            Items = []
        };

        _contextMock.Setup(c => c.Message).Returns(stockFailureEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _orderRepositoryMock.Setup(r => r.GetByIdTrackedAsync(orderId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(order);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        order.Status.Should().Be(OrderStatus.Abandoned);
        order.AbandonReason.Should().Be($"StockReservationFailed: {reason}");
    }
}