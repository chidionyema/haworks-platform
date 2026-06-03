using FluentAssertions;
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

public class PaymentCompletedConsumerTests
{
    private readonly Mock<IOrderRepository> _orderRepositoryMock;
    private readonly Mock<ILogger<PaymentCompletedConsumer>> _loggerMock;
    private readonly Mock<ConsumeContext<PaymentCompletedEvent>> _contextMock;
    private readonly PaymentCompletedConsumer _consumer;

    public PaymentCompletedConsumerTests()
    {
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _loggerMock = new Mock<ILogger<PaymentCompletedConsumer>>();
        _contextMock = new Mock<ConsumeContext<PaymentCompletedEvent>>();
        _consumer = new PaymentCompletedConsumer(_orderRepositoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Consume_ValidPaymentCompleted_MarksOrderPaidAndPublishesEvent()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        var order = Order.Create("user123", 1000L, "USD", sagaId, "key", "test@example.com",
            [new CreateOrderCommand.OrderItemCommand("Product", 1, 1000L, Guid.NewGuid())]);

        var paymentEvent = new PaymentCompletedEvent
        {
            OrderId = orderId,
            PaymentId = paymentId,
            SagaId = sagaId,
            AmountCents = 1000L,
            Currency = "USD",
            Provider = "Stripe",
            TransactionReference = "pi_123"
        };

        _contextMock.Setup(c => c.Message).Returns(paymentEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _orderRepositoryMock.Setup(r => r.GetByIdTrackedAsync(orderId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(order);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        order.Status.Should().Be(OrderStatus.Paid);
        order.PaymentId.Should().Be(paymentId);
        _contextMock.Verify(c => c.Publish(
            It.Is<OrderCompletedEvent>(e =>
                e.OrderId == order.Id &&
                e.PaymentId == paymentId &&
                e.TotalAmountCents == 1000L &&
                e.Currency == "USD" &&
                e.CustomerEmail == "test@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_OrderNotFound_LogsWarningAndDoesNotThrow()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var paymentEvent = new PaymentCompletedEvent
        {
            OrderId = orderId,
            PaymentId = Guid.NewGuid(),
            SagaId = Guid.NewGuid(),
            AmountCents = 1000L,
            Currency = "USD",
            Provider = "Stripe",
            TransactionReference = "pi_123"
        };

        _contextMock.Setup(c => c.Message).Returns(paymentEvent);
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
        _contextMock.Verify(c => c.Publish(It.IsAny<OrderCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_OrderAlreadyInTerminalState_SkipsPublishAndLogs()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        var order = Order.Create("user123", 1000L, "USD", sagaId, "key", "test@example.com",
            [new CreateOrderCommand.OrderItemCommand("Product", 1, 1000L, Guid.NewGuid())]);

        // Mark order as already paid (simulate idempotent processing)
        order.MarkPaid(Guid.NewGuid());

        var paymentEvent = new PaymentCompletedEvent
        {
            OrderId = orderId,
            PaymentId = paymentId,
            SagaId = sagaId,
            AmountCents = 1000L,
            Currency = "USD",
            Provider = "Stripe",
            TransactionReference = "pi_123"
        };

        _contextMock.Setup(c => c.Message).Returns(paymentEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _orderRepositoryMock.Setup(r => r.GetByIdTrackedAsync(orderId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(order);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        order.Status.Should().Be(OrderStatus.Paid);

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
        _contextMock.Verify(c => c.Publish(It.IsAny<OrderCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("user-guid", true)]
    [InlineData("not-a-guid-string", false)]
    [InlineData("", false)]
    public async Task Consume_UserIdParsing_HandlesBothGuidAndStringUserIds(string userId, bool expectGuid)
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        // Use a parseable GUID for the true case
        var actualUserId = expectGuid ? Guid.NewGuid().ToString() : userId;

        var order = Order.Create(actualUserId, 1000L, "USD", sagaId, "key", "test@example.com",
            [new CreateOrderCommand.OrderItemCommand("Product", 1, 1000L, Guid.NewGuid())]);

        var paymentEvent = new PaymentCompletedEvent
        {
            OrderId = orderId,
            PaymentId = paymentId,
            SagaId = sagaId,
            AmountCents = 1000L,
            Currency = "USD",
            Provider = "Stripe",
            TransactionReference = "pi_123"
        };

        _contextMock.Setup(c => c.Message).Returns(paymentEvent);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _orderRepositoryMock.Setup(r => r.GetByIdTrackedAsync(orderId, It.IsAny<CancellationToken>()))
                          .ReturnsAsync(order);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        _contextMock.Verify(c => c.Publish(
            It.Is<OrderCompletedEvent>(e =>
                expectGuid ? e.CustomerId.HasValue : !e.CustomerId.HasValue),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}