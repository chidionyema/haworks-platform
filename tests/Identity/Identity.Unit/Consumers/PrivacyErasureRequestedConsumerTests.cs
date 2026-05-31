using Haworks.Contracts.Privacy;
using Haworks.Identity.Application.Consumers;
using Haworks.Identity.Domain;
using Haworks.Identity.Domain.Interfaces;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Haworks.Identity.Unit.Consumers;

public class PrivacyErasureRequestedConsumerTests
{
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IUserProfileRepository> _userProfileRepositoryMock;
    private readonly Mock<ILogger<PrivacyErasureRequestedConsumer>> _loggerMock;
    private readonly Mock<ConsumeContext<PrivacyErasureRequested>> _contextMock;

    public PrivacyErasureRequestedConsumerTests()
    {
        var userStoreMock = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _userProfileRepositoryMock = new Mock<IUserProfileRepository>();
        _loggerMock = new Mock<ILogger<PrivacyErasureRequestedConsumer>>();
        _contextMock = new Mock<ConsumeContext<PrivacyErasureRequested>>();
    }

    private PrivacyErasureRequestedConsumer CreateConsumer() =>
        new(_userManagerMock.Object, _userProfileRepositoryMock.Object, _loggerMock.Object);

    [Fact]
    public async Task Consume_UserExists_AnonymizesUserAndProfile()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var user = new User
        {
            Id = userId.ToString(),
            UserName = "original-username",
            Email = "original@example.com",
            PhoneNumber = "+1234567890",
            StripeCustomerId = "cus_123",
            CheckoutSessionId = "cs_123",
            IsActive = true
        };

        var userProfile = new UserProfile("Test", "User", "", user.Id);
        userProfile.UpdateAddress("123 Main St", "Apt 1", "Anytown", "CA", "12345");
        userProfile.UpdateProfileInfo("Original bio", "https://example.com");
        userProfile.SetAvatarUrl("https://example.com/avatar.jpg");

        var erasureRequest = new PrivacyErasureRequested
        {
            UserId = userId,
            RequestId = requestId
        };

        _contextMock.Setup(c => c.Message).Returns(erasureRequest);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _userManagerMock.Setup(u => u.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.UpdateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Success);

        _userProfileRepositoryMock.Setup(p => p.GetByUserIdAsync(userId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(userProfile);

        _contextMock.Setup(c => c.Publish(It.IsAny<PrivacyErasureCompleted>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumer = CreateConsumer();

        // Act
        await consumer.Consume(_contextMock.Object);

        // Assert
        // Verify user anonymization
        Assert.Equal($"DELETED-{userId:N}", user.UserName);
        Assert.Equal(user.UserName.ToUpperInvariant(), user.NormalizedUserName);
        Assert.Equal($"deleted-{userId:N}@privacy.invalid", user.Email);
        Assert.Equal(user.Email.ToUpperInvariant(), user.NormalizedEmail);
        Assert.Null(user.PhoneNumber);
        Assert.Null(user.StripeCustomerId);
        Assert.Null(user.CheckoutSessionId);
        Assert.False(user.IsActive);

        // Verify profile anonymization
        Assert.Equal("DELETED", userProfile.FirstName);
        Assert.Equal("DELETED", userProfile.LastName);
        Assert.Equal(string.Empty, userProfile.Bio);
        Assert.Equal(string.Empty, userProfile.Website);
        Assert.Equal(string.Empty, userProfile.AvatarUrl);

        // Verify UserManager.UpdateAsync was called
        _userManagerMock.Verify(u => u.UpdateAsync(user), Times.Once);

        // Verify completion event was published
        _contextMock.Verify(c => c.Publish(It.Is<PrivacyErasureCompleted>(e =>
            e.UserId == userId &&
            e.RequestId == requestId &&
            e.ServiceName == "identity-svc"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_UserNotFound_PublishesCompletionEvent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var erasureRequest = new PrivacyErasureRequested
        {
            UserId = userId,
            RequestId = requestId
        };

        _contextMock.Setup(c => c.Message).Returns(erasureRequest);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _userManagerMock.Setup(u => u.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User)null!);

        _userProfileRepositoryMock.Setup(p => p.GetByUserIdAsync(userId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserProfile)null!);

        _contextMock.Setup(c => c.Publish(It.IsAny<PrivacyErasureCompleted>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumer = CreateConsumer();

        // Act
        await consumer.Consume(_contextMock.Object);

        // Assert
        // Verify no update was attempted
        _userManagerMock.Verify(u => u.UpdateAsync(It.IsAny<User>()), Times.Never);

        // Verify completion event was still published
        _contextMock.Verify(c => c.Publish(It.Is<PrivacyErasureCompleted>(e =>
            e.UserId == userId &&
            e.RequestId == requestId &&
            e.ServiceName == "identity-svc"), It.IsAny<CancellationToken>()), Times.Once);

        // Verify appropriate logging
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("not found — already deleted or never existed")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_UserUpdateFails_PublishesFailureEvent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var user = new User { Id = userId.ToString(), UserName = "testuser", Email = "test@example.com" };

        var erasureRequest = new PrivacyErasureRequested
        {
            UserId = userId,
            RequestId = requestId
        };

        _contextMock.Setup(c => c.Message).Returns(erasureRequest);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _userManagerMock.Setup(u => u.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        // UserManager update fails
        var identityError = new IdentityError { Code = "UpdateFailed", Description = "Update operation failed" };
        _userManagerMock.Setup(u => u.UpdateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Failed(identityError));

        _contextMock.Setup(c => c.Publish(It.IsAny<PrivacyErasureFailed>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumer = CreateConsumer();

        // Act
        await consumer.Consume(_contextMock.Object);

        // Assert
        // Verify failure event was published
        _contextMock.Verify(c => c.Publish(It.Is<PrivacyErasureFailed>(e =>
            e.UserId == userId &&
            e.RequestId == requestId &&
            e.ServiceName == "identity-svc" &&
            e.ErrorMessage.Contains("UserManager.UpdateAsync failed")), It.IsAny<CancellationToken>()), Times.Once);

        // Verify completion event was NOT published
        _contextMock.Verify(c => c.Publish(It.IsAny<PrivacyErasureCompleted>(), It.IsAny<CancellationToken>()), Times.Never);

        // Verify error logging
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to anonymise user")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_ConcurrencyException_ReThrowsForRetry()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var user = new User { Id = userId.ToString(), UserName = "testuser", Email = "test@example.com" };

        var erasureRequest = new PrivacyErasureRequested
        {
            UserId = userId,
            RequestId = requestId
        };

        _contextMock.Setup(c => c.Message).Returns(erasureRequest);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _userManagerMock.Setup(u => u.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        // UserManager update throws concurrency exception
        var concurrencyException = new DbUpdateConcurrencyException("Concurrency conflict");
        _userManagerMock.Setup(u => u.UpdateAsync(It.IsAny<User>()))
            .ThrowsAsync(concurrencyException);

        var consumer = CreateConsumer();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => consumer.Consume(_contextMock.Object));
        Assert.Equal(concurrencyException, exception);

        // Verify appropriate warning was logged
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Concurrency conflict while anonymising user")),
                concurrencyException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Verify no completion or failure events were published
        _contextMock.Verify(c => c.Publish(It.IsAny<PrivacyErasureCompleted>(), It.IsAny<CancellationToken>()), Times.Never);
        _contextMock.Verify(c => c.Publish(It.IsAny<PrivacyErasureFailed>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_ProfileNotFound_ContinuesWithoutError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var user = new User { Id = userId.ToString(), UserName = "testuser", Email = "test@example.com" };

        var erasureRequest = new PrivacyErasureRequested
        {
            UserId = userId,
            RequestId = requestId
        };

        _contextMock.Setup(c => c.Message).Returns(erasureRequest);
        _contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _userManagerMock.Setup(u => u.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.UpdateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Success);

        // Profile not found
        _userProfileRepositoryMock.Setup(p => p.GetByUserIdAsync(userId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserProfile)null!);

        _contextMock.Setup(c => c.Publish(It.IsAny<PrivacyErasureCompleted>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumer = CreateConsumer();

        // Act
        await consumer.Consume(_contextMock.Object);

        // Assert
        // User should still be anonymized
        _userManagerMock.Verify(u => u.UpdateAsync(user), Times.Once);

        // Completion event should be published
        _contextMock.Verify(c => c.Publish(It.Is<PrivacyErasureCompleted>(e =>
            e.UserId == userId &&
            e.RequestId == requestId &&
            e.ServiceName == "identity-svc"), It.IsAny<CancellationToken>()), Times.Once);
    }
}