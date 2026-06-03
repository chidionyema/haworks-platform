using FluentAssertions;
using Haworks.Contracts.Catalog;
using Haworks.Contracts.Orders;
using Haworks.Contracts.Payments;
using Haworks.Orders.Application.Commands;
using Haworks.Orders.Application.Consumers;
using Haworks.Orders.Domain;
using Haworks.Orders.Domain.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Haworks.Orders.Unit.Consumers;

public class PaymentSessionFailedConsumerTests
{
    private readonly Mock<IOrderRepository> _orderRepositoryMock;
    private readonly Mock<ILogger<PaymentSessionFailedConsumer>> _loggerMock;
    private readonly Mock<ConsumeContext<PaymentSessionFailedEvent>> _contextMock;
    private readonly PaymentSessionFailedConsumer _consumer;

    public PaymentSessionFailedConsumerTests()
    {
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _loggerMock = new Mock<ILogger<PaymentSessionFailedConsumer>>();
        _contextMock = new Mock<ConsumeContext<PaymentSessionFailedEvent>>();
        _consumer = new PaymentSessionFailedConsumer(_orderRepositoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Consume_ValidPaymentSessionFailed_MarksOrderAbandonedAndPublishesEvent()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var order = Order.Create("user123", 1000L, "USD", sagaId, "key", "test@example.com",
            [new CreateOrderCommand.OrderItemCommand("Widget", 2, 500L, productId)]);

        var failureEvent = new PaymentSessionFailedEvent
        {
            OrderId = orderId,
            SagaId = sagaId,
            ErrorCode = "card_declined",
            ErrorMessage = "Your card was declined.",
            PaymentId = Guid.NewGuid()
        };

        _contextMock.Setup(c => c.Message).Returns(failureEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _orderRepositoryMock.Setup(r => r.GetByIdTrackedAsync(orderId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(order);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        order.Status.Should().Be(OrderStatus.Abandoned);
        order.AbandonReason.Should().Be("PaymentSessionFailed: card_declined — Your card was declined.");

        _contextMock.Verify(c => c.Publish(
            It.Is<OrderAbandonedEvent>(e =>
                e.OrderId == order.Id &&
                e.SagaId == sagaId &&
                e.CustomerEmail == "test@example.com" &&
                e.PreviousStatus == "Created" &&
                e.Items.Count == 1 &&
                e.Items[0].ProductId == productId &&
                e.Items[0].ProductName == "Widget" &&
                e.Items[0].Quantity == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_OrderNotFound_LogsWarningAndDoesNotThrow()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var failureEvent = new PaymentSessionFailedEvent
        {
            OrderId = orderId,
            SagaId = Guid.NewGuid(),
            ErrorCode = "network_error",
            ErrorMessage = "Payment gateway timeout",
            PaymentId = Guid.NewGuid()
        };

        _contextMock.Setup(c => c.Message).Returns(failureEvent);
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

        var failureEvent = new PaymentSessionFailedEvent
        {
            OrderId = orderId,
            SagaId = sagaId,
            ErrorCode = "expired",
            ErrorMessage = "Session expired",
            PaymentId = Guid.NewGuid()
        };

        _contextMock.Setup(c => c.Message).Returns(failureEvent);
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
    [InlineData("card_declined", "Card was declined")]
    [InlineData("insufficient_funds", "Insufficient funds in account")]
    [InlineData("network_error", "Gateway timeout")]
    public async Task Consume_DifferentErrorCodes_FormatsAbandonReasonCorrectly(string errorCode, string errorMessage)
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        var order = Order.Create("user123", 1000L, "USD", sagaId, "key", "test@example.com",
            [new CreateOrderCommand.OrderItemCommand("Product", 1, 1000L, Guid.NewGuid())]);

        var failureEvent = new PaymentSessionFailedEvent
        {
            OrderId = orderId,
            SagaId = sagaId,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            PaymentId = Guid.NewGuid()
        };

        _contextMock.Setup(c => c.Message).Returns(failureEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _orderRepositoryMock.Setup(r => r.GetByIdTrackedAsync(orderId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(order);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        order.Status.Should().Be(OrderStatus.Abandoned);
        order.AbandonReason.Should().Be($"PaymentSessionFailed: {errorCode} — {errorMessage}");
    }
}