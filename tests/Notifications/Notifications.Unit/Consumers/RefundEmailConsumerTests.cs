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
        _sut = new RefundEmailConsumer(_mediator.Object, NullLogger<RefundEmailConsumer>.Instance);
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
            AmountCents = 5999,
            Currency = "USD",
            CustomerEmail = "customer@example.com"
        };

        var context = new Mock<ConsumeContext<RefundCompletedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);

        SendNotificationCommand? capturedCommand = null;
        _mediator
            .Setup(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest, CancellationToken>((cmd, _) => capturedCommand = cmd as SendNotificationCommand)
            .Returns(Task.FromResult(Guid.NewGuid()));

        // Act
        await _sut.Consume(context.Object);

        // Assert
        _mediator.Verify(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Once);

        capturedCommand.Should().NotBeNull();
        capturedCommand!.UserId.Should().BeNull();
        capturedCommand.Recipient.Should().Be("customer@example.com");
        capturedCommand.Channel.Should().Be(NotificationChannel.Email);
        capturedCommand.TemplateId.Should().Be("refund-completed");
        capturedCommand.Priority.Should().Be(NotificationPriority.High);
        capturedCommand.IdempotencyKey.Should().Be($"refund-completed-{refundId}");

        capturedCommand.Variables.Should().ContainKey("RefundId")
            .WhoseValue.Should().Be(refundId);
        capturedCommand.Variables.Should().ContainKey("Amount")
            .WhoseValue.Should().Be(59.99m); // 5999 cents / 100
        capturedCommand.Variables.Should().ContainKey("Currency")
            .WhoseValue.Should().Be("USD");
    }

    [Fact]
    public async Task Consume_RefundCompletedEvent_NoCustomerEmail_SkipsNotification()
    {
        // Arrange
        var evt = new RefundCompletedEvent
        {
            RefundId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            PaymentId = Guid.NewGuid(),
            AmountCents = 5999,
            Currency = "USD",
            CustomerEmail = null
        };

        var context = new Mock<ConsumeContext<RefundCompletedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);

        // Act
        await _sut.Consume(context.Object);

        // Assert
        _mediator.Verify(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_RefundCompletedEvent_EmptyCustomerEmail_SkipsNotification()
    {
        // Arrange
        var evt = new RefundCompletedEvent
        {
            RefundId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            PaymentId = Guid.NewGuid(),
            AmountCents = 5999,
            Currency = "USD",
            CustomerEmail = "   " // whitespace only
        };

        var context = new Mock<ConsumeContext<RefundCompletedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);

        // Act
        await _sut.Consume(context.Object);

        // Assert
        _mediator.Verify(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
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
            FailureCategory = "provider_error",
            FailureDetail = "Insufficient funds in merchant account",
            CustomerEmail = "customer@example.com"
        };

        var context = new Mock<ConsumeContext<RefundFailedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);

        SendNotificationCommand? capturedCommand = null;
        _mediator
            .Setup(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest, CancellationToken>((cmd, _) => capturedCommand = cmd as SendNotificationCommand)
            .Returns(Task.FromResult(Guid.NewGuid()));

        // Act
        await _sut.Consume(context.Object);

        // Assert
        _mediator.Verify(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Once);

        capturedCommand.Should().NotBeNull();
        capturedCommand!.UserId.Should().BeNull();
        capturedCommand.Recipient.Should().Be("customer@example.com");
        capturedCommand.Channel.Should().Be(NotificationChannel.Email);
        capturedCommand.TemplateId.Should().Be("refund-failed");
        capturedCommand.Priority.Should().Be(NotificationPriority.High);
        capturedCommand.IdempotencyKey.Should().Be($"refund-failed-{refundId}");

        capturedCommand.Variables.Should().ContainKey("RefundId")
            .WhoseValue.Should().Be(refundId);
        capturedCommand.Variables.Should().ContainKey("Reason")
            .WhoseValue.Should().Be("Insufficient funds in merchant account");
    }

    [Fact]
    public async Task Consume_RefundFailedEvent_NoCustomerEmail_SkipsNotification()
    {
        // Arrange
        var evt = new RefundFailedEvent
        {
            RefundId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            FailureCategory = "provider_error",
            FailureDetail = "Network timeout",
            CustomerEmail = null
        };

        var context = new Mock<ConsumeContext<RefundFailedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);

        // Act
        await _sut.Consume(context.Object);

        // Assert
        _mediator.Verify(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_RefundStalledEvent_LogsWarningOnly()
    {
        // Arrange
        var refundId = Guid.NewGuid();
        var evt = new RefundStalledEvent
        {
            RefundId = refundId,
            HoursSinceRequest = 48
        };

        var context = new Mock<ConsumeContext<RefundStalledEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);

        // Act
        await _sut.Consume(context.Object);

        // Assert - RefundStalledEvent only logs, does not send customer notification
        _mediator.Verify(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        // Note: In real implementation, we would verify the log message was written
        // This test confirms the handler completes without sending notifications
    }
}