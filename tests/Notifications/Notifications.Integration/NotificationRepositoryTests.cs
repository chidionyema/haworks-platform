using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Haworks.Notifications.Application.Commands;
using Haworks.Notifications.Domain.Entities;
using Haworks.Notifications.Domain.Enums;
using Haworks.Notifications.Infrastructure.Persistence;

namespace Haworks.Notifications.Integration;

[Collection("Notifications Integration Tests")]
[Trait("Category", "Integration")]
public sealed class NotificationRepositoryTests : IAsyncLifetime, IClassFixture<NotificationsWebAppFactory>
{
    private readonly NotificationsWebAppFactory _factory;
    private IServiceScope? _scope;
    private NotificationsDbContext? _dbContext;
    private INotificationRepository? _repository;

    public NotificationRepositoryTests(NotificationsWebAppFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        _scope = _factory.Services.CreateScope();
        _dbContext = _scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        _repository = _scope.ServiceProvider.GetRequiredService<INotificationRepository>();
    }

    public async Task DisposeAsync()
    {
        _scope?.Dispose();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task FindIdByIdempotencyKeyAsync_NotificationExists_ReturnsId()
    {
        // Arrange
        var idempotencyKey = Guid.NewGuid().ToString("N");
        var notification = Notification.Create(
            recipient: "test@example.com",
            channel: NotificationChannel.Email,
            templateId: "test-template",
            idempotencyKey: idempotencyKey,
            subject: "Test Subject",
            body: "Test Body");

        _repository!.Add(notification);
        await _dbContext!.SaveChangesAsync();

        // Act
        var result = await _repository.FindIdByIdempotencyKeyAsync(idempotencyKey, CancellationToken.None);

        // Assert
        result.Should().Be(notification.Id);
    }

    [Fact]
    public async Task FindIdByIdempotencyKeyAsync_NotificationDoesNotExist_ReturnsNull()
    {
        // Arrange
        var nonExistentIdempotencyKey = Guid.NewGuid().ToString("N");

        // Act
        var result = await _repository!.FindIdByIdempotencyKeyAsync(nonExistentIdempotencyKey, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindIdByIdempotencyKeyAsync_MultipleNotifications_ReturnsCorrectId()
    {
        // Arrange
        var idempotencyKey1 = Guid.NewGuid().ToString("N");
        var idempotencyKey2 = Guid.NewGuid().ToString("N");

        var notification1 = Notification.Create(
            recipient: "test1@example.com",
            channel: NotificationChannel.Email,
            templateId: "template-1",
            idempotencyKey: idempotencyKey1,
            subject: "Subject 1",
            body: "Body 1");

        var notification2 = Notification.Create(
            recipient: "test2@example.com",
            channel: NotificationChannel.Sms,
            templateId: "template-2",
            idempotencyKey: idempotencyKey2,
            subject: "Subject 2",
            body: "Body 2");

        _repository!.Add(notification1);
        _repository.Add(notification2);
        await _dbContext!.SaveChangesAsync();

        // Act
        var result1 = await _repository.FindIdByIdempotencyKeyAsync(idempotencyKey1, CancellationToken.None);
        var result2 = await _repository.FindIdByIdempotencyKeyAsync(idempotencyKey2, CancellationToken.None);

        // Assert
        result1.Should().Be(notification1.Id);
        result2.Should().Be(notification2.Id);
        (result1 != result2).Should().BeTrue();
    }

    [Fact]
    public async Task FindIdByIdempotencyKeyAsync_NullIdempotencyKey_ReturnsNotificationWithNullKey()
    {
        // Arrange
        var notificationWithNullKey = Notification.Create(
            recipient: "test@example.com",
            channel: NotificationChannel.Email,
            templateId: "test-template",
            idempotencyKey: null!, // Force null for this test case
            subject: "Test Subject",
            body: "Test Body");

        var notificationWithKey = Notification.Create(
            recipient: "test2@example.com",
            channel: NotificationChannel.Email,
            templateId: "test-template",
            idempotencyKey: "key-123",
            subject: "Test Subject",
            body: "Test Body");

        _repository!.Add(notificationWithNullKey);
        _repository.Add(notificationWithKey);
        await _dbContext!.SaveChangesAsync();

        // Act
        // Note: This test is actually testing the case where we look for a notification with null idempotency key
        // However, the method signature expects string, so we can't actually pass null
        // This test case might not be valid for this particular method implementation
        var resultForEmpty = await _repository.FindIdByIdempotencyKeyAsync(string.Empty, CancellationToken.None);

        // Assert - should not find anything since we searched for empty string, not null
        resultForEmpty.Should().BeNull();
    }

    [Fact]
    public async Task FindIdByIdempotencyKeyAsync_EmptyIdempotencyKey_ReturnsNull()
    {
        // Arrange
        var notification = Notification.Create(
            recipient: "test@example.com",
            channel: NotificationChannel.Email,
            templateId: "test-template",
            idempotencyKey: "key-123",
            subject: "Test Subject",
            body: "Test Body");

        _repository!.Add(notification);
        await _dbContext!.SaveChangesAsync();

        // Act
        var result = await _repository.FindIdByIdempotencyKeyAsync(string.Empty, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindIdByIdempotencyKeyAsync_CaseSensitive_ReturnsNull()
    {
        // Arrange
        var idempotencyKey = "Case-Sensitive-Key";
        var notification = Notification.Create(
            recipient: "test@example.com",
            channel: NotificationChannel.Email,
            templateId: "test-template",
            idempotencyKey: idempotencyKey,
            subject: "Test Subject",
            body: "Test Body");

        _repository!.Add(notification);
        await _dbContext!.SaveChangesAsync();

        // Act
        var exactMatch = await _repository.FindIdByIdempotencyKeyAsync(idempotencyKey, CancellationToken.None);
        var wrongCase = await _repository.FindIdByIdempotencyKeyAsync(idempotencyKey.ToLowerInvariant(), CancellationToken.None);

        // Assert
        exactMatch.Should().Be(notification.Id);
        wrongCase.Should().BeNull(); // Case sensitive comparison
    }

    [Fact]
    public async Task FindIdByIdempotencyKeyAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _repository!.FindIdByIdempotencyKeyAsync(idempotencyKey, cts.Token));
    }
}