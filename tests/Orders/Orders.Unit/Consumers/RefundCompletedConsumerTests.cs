using FluentAssertions;
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

public class RefundCompletedConsumerTests
{
    private readonly Mock<IOrderRepository> _orderRepositoryMock;
    private readonly Mock<ILogger<RefundCompletedConsumer>> _loggerMock;
    private readonly Mock<ConsumeContext<RefundCompletedEvent>> _contextMock;
    private readonly RefundCompletedConsumer _consumer;

    public RefundCompletedConsumerTests()
    {
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _loggerMock = new Mock<ILogger<RefundCompletedConsumer>>();
        _contextMock = new Mock<ConsumeContext<RefundCompletedEvent>>();
        _consumer = new RefundCompletedConsumer(_orderRepositoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Consume_ValidRefundCompleted_MarksOrderRefunded()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var refundId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        var order = Order.Create("user123", 1000L, "USD", sagaId, "key", "test@example.com",
            [new CreateOrderCommand.OrderItemCommand("Product", 1, 1000L, Guid.NewGuid())]);

        // Mark order as paid first (required for refund)
        order.MarkPaid(paymentId);

        var refundEvent = new RefundCompletedEvent
        {
            OrderId = orderId,
            RefundId = refundId,
            PaymentId = paymentId,
            Amount = 1000L,
            Currency = "USD",
            Reason = "Customer requested refund",
            ProcessedAt = DateTime.UtcNow
        };

        _contextMock.Setup(c => c.Message).Returns(refundEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _orderRepositoryMock.Setup(r => r.GetByIdTrackedAsync(orderId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(order);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        order.Status.Should().Be(OrderStatus.Refunded);

        // Verify informational log was written
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Order {orderId} status updated to Refunded")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_OrderNotFound_LogsWarningAndDoesNotThrow()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var refundId = Guid.NewGuid();

        var refundEvent = new RefundCompletedEvent
        {
            OrderId = orderId,
            RefundId = refundId,
            PaymentId = Guid.NewGuid(),
            Amount = 1000L,
            Currency = "USD",
            Reason = "Customer requested refund",
            ProcessedAt = DateTime.UtcNow
        };

        _contextMock.Setup(c => c.Message).Returns(refundEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _orderRepositoryMock.Setup(r => r.GetByIdTrackedAsync(orderId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync((Order?)null);

        // Act & Assert - should not throw
        await _consumer.Consume(_contextMock.Object);

        // Verify warning was logged with both OrderId and RefundId
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Order {orderId} not found for refund {refundId}")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_OrderNotInPaidState_DoesNotMarkRefundedAndDoesNotLog()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var refundId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        // Create order in Created state (not yet paid)
        var order = Order.Create("user123", 1000L, "USD", sagaId, "key", "test@example.com",
            [new CreateOrderCommand.OrderItemCommand("Product", 1, 1000L, Guid.NewGuid())]);

        var refundEvent = new RefundCompletedEvent
        {
            OrderId = orderId,
            RefundId = refundId,
            PaymentId = Guid.NewGuid(),
            Amount = 1000L,
            Currency = "USD",
            Reason = "Customer requested refund",
            ProcessedAt = DateTime.UtcNow
        };

        _contextMock.Setup(c => c.Message).Returns(refundEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _orderRepositoryMock.Setup(r => r.GetByIdTrackedAsync(orderId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(order);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        order.Status.Should().Be(OrderStatus.Created); // Should remain unchanged

        // Verify no informational log was written (MarkRefunded returned false)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("status updated to Refunded")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_OrderAlreadyRefunded_IdempotentBehavior()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var refundId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        var order = Order.Create("user123", 1000L, "USD", sagaId, "key", "test@example.com",
            [new CreateOrderCommand.OrderItemCommand("Product", 1, 1000L, Guid.NewGuid())]);

        // Mark order as paid then refunded (already processed)
        order.MarkPaid(paymentId);
        order.MarkRefunded();

        var refundEvent = new RefundCompletedEvent
        {
            OrderId = orderId,
            RefundId = refundId,
            PaymentId = paymentId,
            Amount = 1000L,
            Currency = "USD",
            Reason = "Customer requested refund",
            ProcessedAt = DateTime.UtcNow
        };

        _contextMock.Setup(c => c.Message).Returns(refundEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _orderRepositoryMock.Setup(r => r.GetByIdTrackedAsync(orderId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(order);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        order.Status.Should().Be(OrderStatus.Refunded); // Should remain Refunded

        // Verify no informational log was written (MarkRefunded returned false - already refunded)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("status updated to Refunded")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_CreatesLogScope_WithOrderIdAndRefundId()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var refundId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        var order = Order.Create("user123", 1000L, "USD", sagaId, "key", "test@example.com",
            [new CreateOrderCommand.OrderItemCommand("Product", 1, 1000L, Guid.NewGuid())]);

        order.MarkPaid(Guid.NewGuid());

        var refundEvent = new RefundCompletedEvent
        {
            OrderId = orderId,
            RefundId = refundId,
            PaymentId = Guid.NewGuid(),
            Amount = 1000L,
            Currency = "USD",
            Reason = "Customer requested refund",
            ProcessedAt = DateTime.UtcNow
        };

        _contextMock.Setup(c => c.Message).Returns(refundEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _orderRepositoryMock.Setup(r => r.GetByIdTrackedAsync(orderId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(order);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert - verify BeginScope was called with the expected dictionary
        _loggerMock.Verify(
            x => x.BeginScope(It.Is<Dictionary<string, object>>(d =>
                d.ContainsKey("OrderId") && d["OrderId"].Equals(orderId) &&
                d.ContainsKey("RefundId") && d["RefundId"].Equals(refundId))),
            Times.Once);
    }
}