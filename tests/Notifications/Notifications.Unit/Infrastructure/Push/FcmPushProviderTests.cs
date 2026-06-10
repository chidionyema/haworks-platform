using FluentAssertions;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Haworks.Notifications.Application.Channels;
using Haworks.Notifications.Infrastructure.Channels.Push.Fcm;

namespace Haworks.Notifications.Unit.Infrastructure.Push;

[Trait("Category", "Unit")]
public sealed class FcmPushProviderTests
{
    private readonly Mock<FirebaseMessaging> _firebaseMessaging = new();
    private readonly FcmPushProvider _sut;

    public FcmPushProviderTests()
    {
        _sut = new FcmPushProvider(
            _firebaseMessaging.Object,
            NullLogger<FcmPushProvider>.Instance);
    }

    [Fact]
    public void Name_ReturnsCorrectProviderName()
    {
        // Act
        var name = _sut.Name;

        // Assert
        name.Should().Be("fcm");
    }

    [Fact]
    public async Task SendAsync_ValidRequest_ReturnsSuccessWithMessageId()
    {
        // Arrange
        var recipient = "fcm-device-token-123";
        var subject = "Test Subject";
        var body = "Test Body";
        var expectedMessageId = "firebase-message-id-456";

        _firebaseMessaging
            .Setup(f => f.SendAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMessageId);

        // Act
        var result = await _sut.SendAsync(recipient, subject, body, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.ProviderMessageId.Should().Be(expectedMessageId);
        result.Error.Should().BeNull();

        _firebaseMessaging.Verify(
            f => f.SendAsync(
                It.Is<Message>(m =>
                    m.Token == recipient &&
                    m.Notification != null &&
                    m.Notification.Title == subject &&
                    m.Notification.Body == body),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_EmptyRecipient_ReturnsNonRetryableError(string? recipient)
    {
        // Arrange
        var subject = "Test Subject";
        var body = "Test Body";

        // Act
        var result = await _sut.SendAsync(recipient!, subject, body, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeFalse();
        result.Error.Should().Be("Recipient token is required.");
        result.ProviderMessageId.Should().BeNull();

        _firebaseMessaging.Verify(
            f => f.SendAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(MessagingErrorCode.InvalidArgument, false)]
    [InlineData(MessagingErrorCode.Unregistered, false)]
    [InlineData(MessagingErrorCode.QuotaExceeded, false)] // Unexpected error code
    [InlineData(MessagingErrorCode.Unavailable, true)]
    [InlineData(MessagingErrorCode.Internal, true)]
    public async Task SendAsync_FirebaseError_ReturnsCorrectRetryability(MessagingErrorCode errorCode, bool expectedRetryable)
    {
        // Arrange
        var recipient = "fcm-device-token-123";
        var subject = "Test Subject";
        var body = "Test Body";
        var exception = CreateFirebaseException(errorCode, "Test error message");

        _firebaseMessaging
            .Setup(f => f.SendAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var result = await _sut.SendAsync(recipient, subject, body, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().Be(expectedRetryable);
        result.Error.Should().Contain(errorCode.ToString());
        result.Error.Should().Contain("Test error message");
        result.ProviderMessageId.Should().BeNull();
    }


    [Fact]
    public async Task SendAsync_UnexpectedException_ReturnsNonRetryableError()
    {
        // Arrange
        var recipient = "fcm-device-token-123";
        var subject = "Test Subject";
        var body = "Test Body";
        var exception = new InvalidOperationException("Unexpected error");

        _firebaseMessaging
            .Setup(f => f.SendAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var result = await _sut.SendAsync(recipient, subject, body, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeFalse();
        result.Error.Should().Contain("FCM unexpected error");
        result.Error.Should().Contain("Unexpected error");
        result.ProviderMessageId.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_WithCancellationToken_PassesToFirebase()
    {
        // Arrange
        var recipient = "fcm-device-token-123";
        var subject = "Test Subject";
        var body = "Test Body";
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        _firebaseMessaging
            .Setup(f => f.SendAsync(It.IsAny<Message>(), token))
            .ReturnsAsync("message-id");

        // Act
        await _sut.SendAsync(recipient, subject, body, token);

        // Assert
        _firebaseMessaging.Verify(
            f => f.SendAsync(It.IsAny<Message>(), token),
            Times.Once);
    }

    private static FirebaseMessagingException CreateFirebaseException(MessagingErrorCode errorCode, string message)
    {
        // Create FirebaseMessagingException using reflection since constructor parameters may vary
        return (FirebaseMessagingException)Activator.CreateInstance(
            typeof(FirebaseMessagingException),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null,
            new object?[] { errorCode, message, null, null },
            null)!;
    }
}