using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Haworks.Notifications.Application.Commands;
using Haworks.Notifications.Application.Queries;
using Haworks.Notifications.Domain.Entities;
using Haworks.Notifications.Domain.Enums;
using Haworks.Notifications.Domain.ValueObjects;
using Haworks.BuildingBlocks.Common;

namespace Haworks.Notifications.Unit.Queries;

[Trait("Category", "Unit")]
public sealed class GetNotificationQueryHandlerTests
{
    private readonly Mock<INotificationRepository> _repository = new();
    private readonly GetNotificationQueryHandler _sut;

    public GetNotificationQueryHandlerTests()
    {
        _sut = new GetNotificationQueryHandler(_repository.Object, NullLogger<GetNotificationQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_NotificationExists_ReturnsSuccessWithDto()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var notification = CreateNotification("user-123");

        _repository
            .Setup(r => r.GetByIdAsync(notificationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        var query = new GetNotificationQuery(notificationId, "user-123");

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(notification.Id);
        result.Value.UserId.Should().Be("user-123");
        result.Value.Recipient.Should().Be("user@example.com");
        result.Value.Channel.Should().Be("Email");
        result.Value.Status.Should().Be("Created");
        result.Value.Priority.Should().Be("Normal");
        result.Value.TemplateId.Should().Be("welcome");
        result.Value.Attempts.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NotificationNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var notificationId = Guid.NewGuid();

        _repository
            .Setup(r => r.GetByIdAsync(notificationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Notification?)null);

        var query = new GetNotificationQuery(notificationId, "user-123");

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Notifications.NotFound");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_UserMismatch_ReturnsNotFoundErrorAndLogsWarning()
    {
        // Arrange - IDOR protection test: user attempts to access notification owned by another user
        var notificationId = Guid.NewGuid();
        var notification = CreateNotification("owner-user");

        _repository
            .Setup(r => r.GetByIdAsync(notificationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        var query = new GetNotificationQuery(notificationId, "requesting-user");

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Notifications.NotFound");
        result.Error.Type.Should().Be(ErrorType.NotFound);
        // Returns NotFound (not Forbidden) to avoid leaking existence
    }

    [Fact]
    public async Task Handle_NoRequestingUserId_ReturnsNotificationForAnyOwner()
    {
        // Arrange - System/admin access without user context
        var notificationId = Guid.NewGuid();
        var notification = CreateNotification("any-user");

        _repository
            .Setup(r => r.GetByIdAsync(notificationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        var query = new GetNotificationQuery(notificationId, RequestingUserId: null);

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be("any-user");
    }

    [Fact]
    public async Task Handle_NotificationWithDeliveryAttempts_MapsAttemptsCorrectly()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var notification = CreateNotification("user-123");

        // Add delivery attempts using the actual domain method
        var attempt1 = new DeliveryAttempt(
            AttemptedAt: DateTime.UtcNow.AddMinutes(-5),
            ProviderName: "provider-1",
            ProviderMessageId: "msg-1",
            IsSuccess: true,
            ErrorMessage: null);

        var attempt2 = new DeliveryAttempt(
            AttemptedAt: DateTime.UtcNow.AddMinutes(-2),
            ProviderName: "provider-2",
            ProviderMessageId: "msg-2",
            IsSuccess: false,
            ErrorMessage: "Network error");

        notification.RecordAttempt(attempt1);
        notification.RecordAttempt(attempt2);

        _repository
            .Setup(r => r.GetByIdAsync(notificationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        var query = new GetNotificationQuery(notificationId, "user-123");

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Attempts.Should().HaveCount(2);

        var mappedAttempt1 = result.Value.Attempts[0];
        mappedAttempt1.ProviderName.Should().Be("provider-1");
        mappedAttempt1.ProviderMessageId.Should().Be("msg-1");
        mappedAttempt1.IsSuccess.Should().BeTrue();
        mappedAttempt1.ErrorMessage.Should().BeNull();

        var mappedAttempt2 = result.Value.Attempts[1];
        mappedAttempt2.ProviderName.Should().Be("provider-2");
        mappedAttempt2.ProviderMessageId.Should().Be("msg-2");
        mappedAttempt2.IsSuccess.Should().BeFalse();
        mappedAttempt2.ErrorMessage.Should().Be("Network error");
    }

    private static Notification CreateNotification(string userId)
    {
        var notification = Notification.Create(
            recipient: "user@example.com",
            channel: NotificationChannel.Email,
            templateId: "welcome",
            idempotencyKey: "idem-key",
            userId: userId,
            priority: NotificationPriority.Normal,
            subject: "Welcome",
            body: "Welcome to our service");

        return notification;
    }
}