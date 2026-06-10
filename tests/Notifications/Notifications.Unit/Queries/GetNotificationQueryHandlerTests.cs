using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Haworks.Notifications.Application.Commands;
using Haworks.Notifications.Application.Queries;
using Haworks.Notifications.Domain.Entities;
using Haworks.Notifications.Domain.Enums;
using Haworks.Notifications.Domain.ValueObjects;

namespace Haworks.Notifications.Unit.Queries;

[Trait("Category", "Unit")]
public sealed class GetNotificationQueryHandlerTests
{
    private readonly Mock<INotificationRepository> _repository = new();
    private readonly GetNotificationQueryHandler _sut;

    public GetNotificationQueryHandlerTests()
    {
        _sut = new GetNotificationQueryHandler(
            _repository.Object,
            NullLogger<GetNotificationQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_NotificationExists_WithNoRequestingUser_ReturnsSuccess()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var notification = CreateTestNotification(notificationId, userId: "owner-123");

        _repository
            .Setup(r => r.GetByIdAsync(notificationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        var query = new GetNotificationQuery(notificationId, RequestingUserId: null);

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(notificationId);
        result.Value.UserId.Should().Be("owner-123");
        result.Value.Status.Should().Be("Created");
    }

    [Fact]
    public async Task Handle_NotificationExists_RequestingUserIsOwner_ReturnsSuccess()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var notification = CreateTestNotification(notificationId, userId: "owner-123");

        _repository
            .Setup(r => r.GetByIdAsync(notificationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        var query = new GetNotificationQuery(notificationId, RequestingUserId: "owner-123");

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(notificationId);
        result.Value.UserId.Should().Be("owner-123");
    }

    [Fact]
    public async Task Handle_NotificationExists_RequestingUserIsNotOwner_ReturnsNotFound()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var notification = CreateTestNotification(notificationId, userId: "owner-123");

        _repository
            .Setup(r => r.GetByIdAsync(notificationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        var query = new GetNotificationQuery(notificationId, RequestingUserId: "other-user-456");

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Notifications.NotFound");
        result.Error.Type.Should().Be(Haworks.BuildingBlocks.Common.ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_NotificationNotFound_ReturnsNotFound()
    {
        // Arrange
        var notificationId = Guid.NewGuid();

        _repository
            .Setup(r => r.GetByIdAsync(notificationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Notification?)null);

        var query = new GetNotificationQuery(notificationId, RequestingUserId: "any-user");

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Notifications.NotFound");
        result.Error.Type.Should().Be(Haworks.BuildingBlocks.Common.ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_NotificationHasDeliveryAttempts_MapsAttemptsCorrectly()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var notification = CreateTestNotification(notificationId, userId: "owner-123");

        // Add delivery attempts using reflection since DeliveryAttempts might be internal
        var attempt1 = CreateDeliveryAttempt(
            attemptedAt: DateTime.UtcNow.AddMinutes(-10),
            providerName: "sendgrid",
            providerMessageId: "msg-123",
            isSuccess: true,
            errorMessage: null);

        var attempt2 = CreateDeliveryAttempt(
            attemptedAt: DateTime.UtcNow.AddMinutes(-5),
            providerName: "ses",
            providerMessageId: null,
            isSuccess: false,
            errorMessage: "Rate limited");

        // Use reflection to set private collection
        var attemptsProperty = typeof(Notification).GetProperty("DeliveryAttempts")!;
        var attempts = (IList<DeliveryAttempt>)attemptsProperty.GetValue(notification)!;
        attempts.Add(attempt1);
        attempts.Add(attempt2);

        _repository
            .Setup(r => r.GetByIdAsync(notificationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        var query = new GetNotificationQuery(notificationId, RequestingUserId: null);

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Attempts.Should().HaveCount(2);

        var firstAttempt = result.Value.Attempts[0];
        firstAttempt.ProviderName.Should().Be("sendgrid");
        firstAttempt.ProviderMessageId.Should().Be("msg-123");
        firstAttempt.IsSuccess.Should().BeTrue();
        firstAttempt.ErrorMessage.Should().BeNull();

        var secondAttempt = result.Value.Attempts[1];
        secondAttempt.ProviderName.Should().Be("ses");
        secondAttempt.IsSuccess.Should().BeFalse();
        secondAttempt.ErrorMessage.Should().Be("Rate limited");
    }

    [Fact]
    public async Task Handle_NotificationWithNullUserId_AllowsAccessForAnyRequestingUser()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        var notification = CreateTestNotification(notificationId, userId: null);

        _repository
            .Setup(r => r.GetByIdAsync(notificationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        var query = new GetNotificationQuery(notificationId, RequestingUserId: "any-user");

        // Act
        var result = await _sut.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().BeNull();
    }

    private static Notification CreateTestNotification(Guid id, string? userId)
    {
        var notification = Notification.Create(
            recipient: "test@example.com",
            channel: NotificationChannel.Email,
            templateId: "test-template",
            idempotencyKey: Guid.NewGuid().ToString("N"),
            subject: "Test Subject",
            body: "Test Body");

        // Set the ID and UserId using reflection since they might be private setters
        var idProperty = typeof(Notification).GetProperty("Id")!;
        if (idProperty.CanWrite)
        {
            idProperty.SetValue(notification, id);
        }
        else
        {
            var idField = typeof(Notification).GetField("_id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            idField?.SetValue(notification, id);
        }

        if (userId != null)
        {
            var userIdProperty = typeof(Notification).GetProperty("UserId");
            if (userIdProperty?.CanWrite == true)
            {
                userIdProperty.SetValue(notification, userId);
            }
            else
            {
                var userIdField = typeof(Notification).GetField("_userId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                userIdField?.SetValue(notification, userId);
            }
        }

        return notification;
    }

    private static DeliveryAttempt CreateDeliveryAttempt(
        DateTime attemptedAt,
        string providerName,
        string? providerMessageId,
        bool isSuccess,
        string? errorMessage)
    {
        // Create delivery attempt - this may need adjustment based on the actual constructor
        var constructor = typeof(DeliveryAttempt).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)[0];
        var parameters = constructor.GetParameters();

        if (parameters.Length == 5)
        {
            return (DeliveryAttempt)constructor.Invoke(new object?[] { attemptedAt, providerName, providerMessageId, isSuccess, errorMessage });
        }

        // Fallback: try to create with available public methods
        var attempt = (DeliveryAttempt)Activator.CreateInstance(typeof(DeliveryAttempt), true)!;

        // Set properties via reflection
        var properties = typeof(DeliveryAttempt).GetProperties();

        foreach (var prop in properties)
        {
            if (prop.Name == nameof(DeliveryAttempt.AttemptedAt) && prop.CanWrite)
                prop.SetValue(attempt, attemptedAt);
            else if (prop.Name == nameof(DeliveryAttempt.ProviderName) && prop.CanWrite)
                prop.SetValue(attempt, providerName);
            else if (prop.Name == nameof(DeliveryAttempt.ProviderMessageId) && prop.CanWrite)
                prop.SetValue(attempt, providerMessageId);
            else if (prop.Name == nameof(DeliveryAttempt.IsSuccess) && prop.CanWrite)
                prop.SetValue(attempt, isSuccess);
            else if (prop.Name == nameof(DeliveryAttempt.ErrorMessage) && prop.CanWrite)
                prop.SetValue(attempt, errorMessage);
        }

        return attempt;
    }
}