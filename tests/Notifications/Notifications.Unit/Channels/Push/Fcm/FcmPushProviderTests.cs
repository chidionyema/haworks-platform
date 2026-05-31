using FluentAssertions;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Haworks.Notifications.Infrastructure.Channels.Push.Fcm;

namespace Haworks.Notifications.Unit.Channels.Push.Fcm;

[Trait("Category", "Unit")]
public sealed class FcmPushProviderTests
{
    private readonly Mock<FirebaseMessaging> _firebaseMessaging = new();
    private readonly FcmPushProvider _sut;

    public FcmPushProviderTests()
    {
        _sut = new FcmPushProvider(_firebaseMessaging.Object, NullLogger<FcmPushProvider>.Instance);
    }

    [Fact]
    public void Name_ReturnsCorrectProviderName()
    {
        // Act & Assert
        _sut.Name.Should().Be("fcm");
    }

    [Fact]
    public void Constructor_NullMessaging_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new FcmPushProvider(null!, NullLogger<FcmPushProvider>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("messaging");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new FcmPushProvider(_firebaseMessaging.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task SendAsync_ValidInput_ReturnsSuccessWithMessageId()
    {
        // Arrange
        var recipient = "device-token-12345";
        var subject = "Push Title";
        var body = "Push message body";
        var expectedMessageId = "fcm-msg-abc123";

        _firebaseMessaging
            .Setup(m => m.SendAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMessageId);

        // Act
        var result = await _sut.SendAsync(recipient, subject, body, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.ProviderMessageId.Should().Be(expectedMessageId);
        result.Error.Should().BeNull();
        result.IsRetryable.Should().BeFalse();

        // Verify the message was constructed correctly
        _firebaseMessaging.Verify(
            m => m.SendAsync(
                It.Is<Message>(msg =>
                    msg.Token == recipient &&
                    msg.Notification!.Title == subject &&
                    msg.Notification.Body == body),
                CancellationToken.None),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_EmptyRecipient_ReturnsNonRetryableError(string? emptyRecipient)
    {
        // Act
        var result = await _sut.SendAsync(emptyRecipient!, "subject", "body", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeFalse();
        result.Error.Should().Be("Recipient token is required.");
        result.ProviderMessageId.Should().BeNull();

        // Verify Firebase was not called
        _firebaseMessaging.Verify(
            m => m.SendAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(MessagingErrorCode.InvalidArgument)]
    [InlineData(MessagingErrorCode.Unregistered)]
    public async Task SendAsync_NonRetryableFirebaseError_ReturnsNonRetryableResult(MessagingErrorCode errorCode)
    {
        // Arrange
        var exception = new FirebaseMessagingException(
            errorCode,
            $"Firebase error: {errorCode}",
            null,
            null);

        _firebaseMessaging
            .Setup(m => m.SendAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var result = await _sut.SendAsync("device-token", "subject", "body", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeFalse();
        result.Error.Should().Contain($"FCM error: {errorCode}");
        result.ProviderMessageId.Should().BeNull();
    }

    [Theory]
    [InlineData(MessagingErrorCode.Unavailable)]
    [InlineData(MessagingErrorCode.Internal)]
    public async Task SendAsync_RetryableFirebaseError_ReturnsRetryableResult(MessagingErrorCode errorCode)
    {
        // Arrange
        var exception = new FirebaseMessagingException(
            errorCode,
            $"Firebase error: {errorCode}",
            null,
            null);

        _firebaseMessaging
            .Setup(m => m.SendAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var result = await _sut.SendAsync("device-token", "subject", "body", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeTrue();
        result.Error.Should().Contain($"FCM error: {errorCode}");
        result.ProviderMessageId.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_UnexpectedFirebaseError_ReturnsNonRetryableResult()
    {
        // Arrange
        var exception = new FirebaseMessagingException(
            MessagingErrorCode.ThirdPartyAuth, // Unknown error code not explicitly handled
            "Unexpected Firebase error",
            null,
            null);

        _firebaseMessaging
            .Setup(m => m.SendAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var result = await _sut.SendAsync("device-token", "subject", "body", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeFalse();
        result.Error.Should().Contain("FCM unexpected error: ThirdPartyAuth");
        result.ProviderMessageId.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_GenericException_ReturnsNonRetryableResult()
    {
        // Arrange
        var exception = new InvalidOperationException("Some other error");

        _firebaseMessaging
            .Setup(m => m.SendAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var result = await _sut.SendAsync("device-token", "subject", "body", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeFalse();
        result.Error.Should().Be("FCM unexpected error: Some other error");
        result.ProviderMessageId.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_UsesCancellationToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _firebaseMessaging
            .Setup(m => m.SendAsync(It.IsAny<Message>(), cancellationToken))
            .ReturnsAsync("msg-id");

        // Act
        await _sut.SendAsync("device-token", "subject", "body", cancellationToken);

        // Assert
        _firebaseMessaging.Verify(
            m => m.SendAsync(It.IsAny<Message>(), cancellationToken),
            Times.Once);
    }
}