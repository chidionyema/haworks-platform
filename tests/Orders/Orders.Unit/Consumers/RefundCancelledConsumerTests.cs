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

public class RefundCancelledConsumerTests
{
    private readonly Mock<IOrderRepository> _orderRepositoryMock;
    private readonly Mock<ILogger<RefundCancelledConsumer>> _loggerMock;
    private readonly Mock<ConsumeContext<RefundCancelledEvent>> _contextMock;
    private readonly RefundCancelledConsumer _consumer;

    public RefundCancelledConsumerTests()
    {
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _loggerMock = new Mock<ILogger<RefundCancelledConsumer>>();
        _contextMock = new Mock<ConsumeContext<RefundCancelledEvent>>();
        _consumer = new RefundCancelledConsumer(_orderRepositoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Consume_ValidRefundCancelled_RevertsOrderToPaid()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var refundId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        var order = Order.Create("user123", 1000L, "USD", sagaId, "key", "test@example.com",
            [new CreateOrderCommand.OrderItemCommand("Product", 1, 1000L, Guid.NewGuid())]);

        // Mark order as paid then refunded (for refund cancellation to work)
        order.MarkPaid(paymentId);
        order.MarkRefunded();

        var refundCancelledEvent = new RefundCancelledEvent
        {
            OrderId = orderId,
            RefundId = refundId,
            PaymentId = paymentId,
            Amount = 1000L,
            Currency = "USD",
            Reason = "Merchant cancelled refund",
            CancelledAt = DateTime.UtcNow
        };

        _contextMock.Setup(c => c.Message).Returns(refundCancelledEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _orderRepositoryMock.Setup(r => r.GetByIdTrackedAsync(orderId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(order);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        order.Status.Should().Be(OrderStatus.Paid); // Reverted from Refunded

        // Verify informational log was written
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Order {orderId} status reverted to Paid")),
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

        var refundCancelledEvent = new RefundCancelledEvent
        {
            OrderId = orderId,
            RefundId = refundId,
            PaymentId = Guid.NewGuid(),
            Amount = 1000L,
            Currency = "USD",
            Reason = "Merchant cancelled refund",
            CancelledAt = DateTime.UtcNow
        };

        _contextMock.Setup(c => c.Message).Returns(refundCancelledEvent);
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
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Order {orderId} not found for cancelled refund {refundId}")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_OrderNotInRefundedState_DoesNotRevertAndDoesNotLog()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var refundId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        var order = Order.Create("user123", 1000L, "USD", sagaId, "key", "test@example.com",
            [new CreateOrderCommand.OrderItemCommand("Product", 1, 1000L, Guid.NewGuid())]);

        // Mark order as paid (but not refunded)
        order.MarkPaid(paymentId);

        var refundCancelledEvent = new RefundCancelledEvent
        {
            OrderId = orderId,
            RefundId = refundId,
            PaymentId = paymentId,
            Amount = 1000L,
            Currency = "USD",
            Reason = "Merchant cancelled refund",
            CancelledAt = DateTime.UtcNow
        };

        _contextMock.Setup(c => c.Message).Returns(refundCancelledEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _orderRepositoryMock.Setup(r => r.GetByIdTrackedAsync(orderId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(order);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        order.Status.Should().Be(OrderStatus.Paid); // Should remain Paid (no change)

        // Verify no informational log was written (RevertToPaid returned false)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("status reverted to Paid")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_OrderInCreatedState_DoesNotRevert()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var refundId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        // Create order in Created state (not paid or refunded)
        var order = Order.Create("user123", 1000L, "USD", sagaId, "key", "test@example.com",
            [new CreateOrderCommand.OrderItemCommand("Product", 1, 1000L, Guid.NewGuid())]);

        var refundCancelledEvent = new RefundCancelledEvent
        {
            OrderId = orderId,
            RefundId = refundId,
            PaymentId = Guid.NewGuid(),
            Amount = 1000L,
            Currency = "USD",
            Reason = "Merchant cancelled refund",
            CancelledAt = DateTime.UtcNow
        };

        _contextMock.Setup(c => c.Message).Returns(refundCancelledEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _orderRepositoryMock.Setup(r => r.GetByIdTrackedAsync(orderId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(order);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        order.Status.Should().Be(OrderStatus.Created); // Should remain unchanged

        // Verify no informational log was written (RevertToPaid returned false)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("status reverted to Paid")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_IdempotentBehavior_RefundAlreadyCancelled()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var refundId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        var order = Order.Create("user123", 1000L, "USD", sagaId, "key", "test@example.com",
            [new CreateOrderCommand.OrderItemCommand("Product", 1, 1000L, Guid.NewGuid())]);

        // Mark order as paid -> refunded -> reverted to paid (already processed refund cancellation)
        order.MarkPaid(paymentId);
        order.MarkRefunded();
        order.RevertToPaid();

        var refundCancelledEvent = new RefundCancelledEvent
        {
            OrderId = orderId,
            RefundId = refundId,
            PaymentId = paymentId,
            Amount = 1000L,
            Currency = "USD",
            Reason = "Merchant cancelled refund",
            CancelledAt = DateTime.UtcNow
        };

        _contextMock.Setup(c => c.Message).Returns(refundCancelledEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _orderRepositoryMock.Setup(r => r.GetByIdTrackedAsync(orderId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(order);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        order.Status.Should().Be(OrderStatus.Paid); // Should remain Paid

        // Verify no informational log was written (RevertToPaid returned false - already Paid)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("status reverted to Paid")),
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
        order.MarkRefunded();

        var refundCancelledEvent = new RefundCancelledEvent
        {
            OrderId = orderId,
            RefundId = refundId,
            PaymentId = Guid.NewGuid(),
            Amount = 1000L,
            Currency = "USD",
            Reason = "Merchant cancelled refund",
            CancelledAt = DateTime.UtcNow
        };

        _contextMock.Setup(c => c.Message).Returns(refundCancelledEvent);
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