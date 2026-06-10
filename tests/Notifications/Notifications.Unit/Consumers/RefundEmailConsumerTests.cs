using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Haworks.Contracts.Payments;
using Haworks.Notifications.Application.Commands;
using Haworks.Notifications.Application.Consumers;
using Haworks.Notifications.Domain.Enums;
using MediatR;

namespace Haworks.Notifications.Unit.Consumers;

[Trait("Category", "Unit")]
public sealed class RefundEmailConsumerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly RefundEmailConsumer _sut;

    public RefundEmailConsumerTests()
    {
        _sut = new RefundEmailConsumer(
            _mediator.Object,
            NullLogger<RefundEmailConsumer>.Instance);
    }

    [Fact]
    public async Task Consume_RefundCompletedEvent_WithCustomerEmail_SendsNotification()
    {
        // Arrange
        var refundId = Guid.NewGuid();
        var evt = new RefundCompletedEvent
        {
            RefundId = refundId,
            OrderId = Guid.NewGuid(),
            PaymentId = Guid.NewGuid(),
            AmountCents = 4999,
            Currency = "USD",
            CustomerEmail = "customer@example.com"
        };

        var ctx = ConsumeContextFor(evt);

        // Act
        await _sut.Consume(ctx.Object);

        // Assert
        _mediator.Verify(
            m => m.Send(
                It.Is<SendNotificationCommand>(cmd =>
                    cmd.UserId == null &&
                    cmd.Recipient == "customer@example.com" &&
                    cmd.Channel == NotificationChannel.Email &&
                    cmd.TemplateId == "refund-completed" &&
                    cmd.Priority == NotificationPriority.High &&
                    cmd.Variables.ContainsKey("RefundId") &&
                    cmd.Variables["RefundId"].Equals(refundId) &&
                    cmd.Variables["Amount"].Equals(49.99m) &&
                    cmd.Variables["Currency"].Equals("USD") &&
                    cmd.IdempotencyKey == $"refund-completed-{refundId}"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_RefundCompletedEvent_WithoutCustomerEmail_SkipsNotification()
    {
        // Arrange
        var evt = new RefundCompletedEvent
        {
            RefundId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            PaymentId = Guid.NewGuid(),
            AmountCents = 1000,
            Currency = "USD",
            CustomerEmail = null
        };

        var ctx = ConsumeContextFor(evt);

        // Act
        await _sut.Consume(ctx.Object);

        // Assert
        _mediator.Verify(
            m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_RefundCompletedEvent_WithEmptyCustomerEmail_SkipsNotification()
    {
        // Arrange
        var evt = new RefundCompletedEvent
        {
            RefundId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            PaymentId = Guid.NewGuid(),
            AmountCents = 1000,
            Currency = "USD",
            CustomerEmail = "   "
        };

        var ctx = ConsumeContextFor(evt);

        // Act
        await _sut.Consume(ctx.Object);

        // Assert
        _mediator.Verify(
            m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_RefundFailedEvent_WithCustomerEmail_SendsNotification()
    {
        // Arrange
        var refundId = Guid.NewGuid();
        var evt = new RefundFailedEvent
        {
            RefundId = refundId,
            OrderId = Guid.NewGuid(),
            FailureCategory = "PROVIDER_ERROR",
            FailureDetail = "Insufficient funds",
            CustomerEmail = "customer@example.com"
        };

        var ctx = ConsumeContextFor(evt);

        // Act
        await _sut.Consume(ctx.Object);

        // Assert
        _mediator.Verify(
            m => m.Send(
                It.Is<SendNotificationCommand>(cmd =>
                    cmd.UserId == null &&
                    cmd.Recipient == "customer@example.com" &&
                    cmd.Channel == NotificationChannel.Email &&
                    cmd.TemplateId == "refund-failed" &&
                    cmd.Priority == NotificationPriority.High &&
                    cmd.Variables.ContainsKey("RefundId") &&
                    cmd.Variables["RefundId"].Equals(refundId) &&
                    cmd.Variables["Reason"].Equals("Insufficient funds") &&
                    cmd.IdempotencyKey == $"refund-failed-{refundId}"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_RefundFailedEvent_WithoutCustomerEmail_SkipsNotification()
    {
        // Arrange
        var evt = new RefundFailedEvent
        {
            RefundId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            FailureCategory = "PROVIDER_ERROR",
            FailureDetail = "Test failure",
            CustomerEmail = null
        };

        var ctx = ConsumeContextFor(evt);

        // Act
        await _sut.Consume(ctx.Object);

        // Assert
        _mediator.Verify(
            m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consume_RefundStalledEvent_DoesNotSendCustomerNotification()
    {
        // Arrange
        var evt = new RefundStalledEvent
        {
            RefundId = Guid.NewGuid(),
            HoursSinceRequest = 24
        };

        var ctx = ConsumeContextFor(evt);

        // Act
        await _sut.Consume(ctx.Object);

        // Assert
        _mediator.Verify(
            m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static Mock<ConsumeContext<T>> ConsumeContextFor<T>(T evt) where T : class
    {
        var ctx = new Mock<ConsumeContext<T>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx;
    }
}