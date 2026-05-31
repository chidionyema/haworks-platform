using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Haworks.Notifications.Application.Webhooks;
using MediatR;

namespace Haworks.Notifications.Unit.Webhooks;

[Trait("Category", "Unit")]
public sealed class NotificationWebhookValidatedConsumerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly NotificationWebhookValidatedConsumer _sut;

    public NotificationWebhookValidatedConsumerTests()
    {
        _sut = new NotificationWebhookValidatedConsumer(_mediator.Object, NullLogger<NotificationWebhookValidatedConsumer>.Instance);
    }

    [Fact]
    public async Task Consume_ValidatedWebhookEvent_SendsUpdateCommand()
    {
        // Arrange
        var evt = new NotificationWebhookValidatedEvent
        {
            Provider = "SES",
            ProviderEventId = "msg-12345",
            EventType = "Delivery",
            RawPayload = "{\"eventType\":\"delivery\",\"mail\":{\"messageId\":\"msg-12345\"}}",
            Signature = "sha256=abc123"
        };

        var context = new Mock<ConsumeContext<NotificationWebhookValidatedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        UpdateNotificationStatusFromWebhookCommand? capturedCommand = null;
        _mediator
            .Setup(m => m.Send(It.IsAny<UpdateNotificationStatusFromWebhookCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest, CancellationToken>((cmd, _) => capturedCommand = cmd as UpdateNotificationStatusFromWebhookCommand)
            .ReturnsAsync(new Haworks.BuildingBlocks.Common.Result());

        // Act
        await _sut.Consume(context.Object);

        // Assert
        _mediator.Verify(m => m.Send(It.IsAny<UpdateNotificationStatusFromWebhookCommand>(), CancellationToken.None), Times.Once);

        capturedCommand.Should().NotBeNull();
        capturedCommand!.Provider.Should().Be("SES");
        capturedCommand.ProviderEventId.Should().Be("msg-12345");
        capturedCommand.EventType.Should().Be("Delivery");
        capturedCommand.RawPayload.Should().Be("{\"eventType\":\"delivery\",\"mail\":{\"messageId\":\"msg-12345\"}}");
    }

    [Fact]
    public async Task Consume_TwilioWebhookEvent_SendsUpdateCommand()
    {
        // Arrange
        var evt = new NotificationWebhookValidatedEvent
        {
            Provider = "Twilio",
            ProviderEventId = "SMxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
            EventType = "delivered",
            RawPayload = "{\"MessageSid\":\"SMxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\",\"MessageStatus\":\"delivered\"}",
            Signature = null
        };

        var context = new Mock<ConsumeContext<NotificationWebhookValidatedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        UpdateNotificationStatusFromWebhookCommand? capturedCommand = null;
        _mediator
            .Setup(m => m.Send(It.IsAny<UpdateNotificationStatusFromWebhookCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest, CancellationToken>((cmd, _) => capturedCommand = cmd as UpdateNotificationStatusFromWebhookCommand)
            .ReturnsAsync(new Haworks.BuildingBlocks.Common.Result());

        // Act
        await _sut.Consume(context.Object);

        // Assert
        capturedCommand.Should().NotBeNull();
        capturedCommand!.Provider.Should().Be("Twilio");
        capturedCommand.ProviderEventId.Should().Be("SMxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
        capturedCommand.EventType.Should().Be("delivered");
        capturedCommand.RawPayload.Should().Contain("MessageStatus\":\"delivered");
    }

    [Fact]
    public async Task Consume_SendGridWebhookEvent_SendsUpdateCommand()
    {
        // Arrange
        var evt = new NotificationWebhookValidatedEvent
        {
            Provider = "SendGrid",
            ProviderEventId = "sg_message_id_12345",
            EventType = "open",
            RawPayload = "[{\"event\":\"open\",\"sg_message_id\":\"sg_message_id_12345\",\"timestamp\":1693478400}]"
        };

        var context = new Mock<ConsumeContext<NotificationWebhookValidatedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        _mediator
            .Setup(m => m.Send(It.IsAny<UpdateNotificationStatusFromWebhookCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Haworks.BuildingBlocks.Common.Result());

        // Act
        await _sut.Consume(context.Object);

        // Assert
        _mediator.Verify(
            m => m.Send(
                It.Is<UpdateNotificationStatusFromWebhookCommand>(cmd =>
                    cmd.Provider == "SendGrid" &&
                    cmd.ProviderEventId == "sg_message_id_12345" &&
                    cmd.EventType == "open"),
                CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task Consume_UsesCancellationTokenFromContext()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        var evt = new NotificationWebhookValidatedEvent
        {
            Provider = "SES",
            ProviderEventId = "test-id",
            EventType = "bounce",
            RawPayload = "{}"
        };

        var context = new Mock<ConsumeContext<NotificationWebhookValidatedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(cancellationToken);

        _mediator
            .Setup(m => m.Send(It.IsAny<UpdateNotificationStatusFromWebhookCommand>(), cancellationToken))
            .ReturnsAsync(new Haworks.BuildingBlocks.Common.Result());

        // Act
        await _sut.Consume(context.Object);

        // Assert
        _mediator.Verify(m => m.Send(It.IsAny<UpdateNotificationStatusFromWebhookCommand>(), cancellationToken), Times.Once);
    }

    [Fact]
    public async Task Consume_EmptyRawPayload_StillSendsCommand()
    {
        // Arrange
        var evt = new NotificationWebhookValidatedEvent
        {
            Provider = "TestProvider",
            ProviderEventId = "test-123",
            EventType = "unknown",
            RawPayload = "",
            Signature = null
        };

        var context = new Mock<ConsumeContext<NotificationWebhookValidatedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        _mediator
            .Setup(m => m.Send(It.IsAny<UpdateNotificationStatusFromWebhookCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Haworks.BuildingBlocks.Common.Result());

        // Act
        await _sut.Consume(context.Object);

        // Assert
        _mediator.Verify(m => m.Send(It.IsAny<UpdateNotificationStatusFromWebhookCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}