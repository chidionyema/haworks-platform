using Haworks.Contracts.Privacy;
using Haworks.Identity.Application.Consumers;
using Haworks.Identity.Domain;
using Haworks.Identity.Domain.Interfaces;
using Haworks.BuildingBlocks.Testing;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit.Abstractions;
using Xunit;

namespace Haworks.Identity.UnitTests.Consumers;

public class PrivacyErasureRequestedConsumerTests : TestBase
{
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IUserProfileRepository> _userProfileRepositoryMock;
    private readonly Mock<ILogger<PrivacyErasureRequestedConsumer>> _loggerMock;
    private readonly Mock<ConsumeContext<PrivacyErasureRequested>> _contextMock;
    private readonly PrivacyErasureRequestedConsumer _consumer;

    public PrivacyErasureRequestedConsumerTests(ITestOutputHelper output) : base(output)
    {
        var userStoreMock = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _userProfileRepositoryMock = new Mock<IUserProfileRepository>();
        _loggerMock = new Mock<ILogger<PrivacyErasureRequestedConsumer>>();
        _contextMock = new Mock<ConsumeContext<PrivacyErasureRequested>>();

        _consumer = new PrivacyErasureRequestedConsumer(
            _userManagerMock.Object,
            _userProfileRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Consume_WhenUserExists_AnonymizesUserAndPublishesSuccess()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var message = new PrivacyErasureRequested
        {
            UserId = userId,
            RequestId = requestId
        };

        _contextMock
            .Setup(c => c.Message)
            .Returns(message);

        _contextMock
            .Setup(c => c.CancellationToken)
            .Returns(CancellationToken.None);

        var user = CreateUser("test@example.com", "testuser");
        user.Id = userId.ToString();
        user.StripeCustomerId = "cus_123";
        user.CheckoutSessionId = "cs_123";
        user.PhoneNumber = "123-456-7890";

        _userManagerMock
            .Setup(u => u.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock
            .Setup(u => u.UpdateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Success);

        var profile = CreateUserProfile(userId.ToString());
        _userProfileRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId.ToString(), CancellationToken.None))
            .ReturnsAsync(profile.Object);

        _contextMock
            .Setup(c => c.Publish(It.IsAny<PrivacyErasureCompleted>(), CancellationToken.None))
            .Returns(Task.CompletedTask);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        // Verify user anonymization
        Assert.Equal($"DELETED-{userId:N}", user.UserName);
        Assert.Equal($"DELETED-{userId:N}", user.NormalizedUserName);
        Assert.Equal($"deleted-{userId:N}@privacy.invalid", user.Email);
        Assert.Equal($"deleted-{userId:N}@privacy.invalid".ToUpperInvariant(), user.NormalizedEmail);
        Assert.Null(user.PhoneNumber);
        Assert.Null(user.StripeCustomerId);
        Assert.Null(user.CheckoutSessionId);
        Assert.False(user.IsActive);

        // Verify profile anonymization
        profile.Verify(p => p.UpdatePersonalInfo("DELETED", "DELETED", ""), Times.Once);
        profile.Verify(p => p.UpdateAddress("", "", "", "", ""), Times.Once);
        profile.Verify(p => p.UpdateProfileInfo(It.IsAny<string>(), ""), Times.Once);
        profile.Verify(p => p.SetAvatarUrl(""), Times.Once);

        // Verify success event published
        _contextMock.Verify(c => c.Publish(
            It.Is<PrivacyErasureCompleted>(e =>
                e.RequestId == requestId &&
                e.UserId == userId &&
                e.ServiceName == "identity-svc"),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Consume_WhenUserNotFound_LogsAndPublishesSuccess()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var message = new PrivacyErasureRequested
        {
            UserId = userId,
            RequestId = requestId
        };

        _contextMock
            .Setup(c => c.Message)
            .Returns(message);

        _contextMock
            .Setup(c => c.CancellationToken)
            .Returns(CancellationToken.None);

        _userManagerMock
            .Setup(u => u.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        _userProfileRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId.ToString(), CancellationToken.None))
            .ReturnsAsync((UserProfile?)null);

        _contextMock
            .Setup(c => c.Publish(It.IsAny<PrivacyErasureCompleted>(), CancellationToken.None))
            .Returns(Task.CompletedTask);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("not found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Should still publish success
        _contextMock.Verify(c => c.Publish(
            It.Is<PrivacyErasureCompleted>(e =>
                e.RequestId == requestId &&
                e.UserId == userId &&
                e.ServiceName == "identity-svc"),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Consume_WhenUserUpdateFails_PublishesFailureEvent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var message = new PrivacyErasureRequested
        {
            UserId = userId,
            RequestId = requestId
        };

        _contextMock
            .Setup(c => c.Message)
            .Returns(message);

        _contextMock
            .Setup(c => c.CancellationToken)
            .Returns(CancellationToken.None);

        var user = CreateUser("test@example.com", "testuser");
        user.Id = userId.ToString();

        _userManagerMock
            .Setup(u => u.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        var updateError = new IdentityError { Description = "Update failed" };
        _userManagerMock
            .Setup(u => u.UpdateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Failed(updateError));

        _contextMock
            .Setup(c => c.Publish(It.IsAny<PrivacyErasureFailed>(), CancellationToken.None))
            .Returns(Task.CompletedTask);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        _contextMock.Verify(c => c.Publish(
            It.Is<PrivacyErasureFailed>(e =>
                e.RequestId == requestId &&
                e.UserId == userId &&
                e.ServiceName == "identity-svc" &&
                e.ErrorMessage.Contains("Update failed")),
            CancellationToken.None), Times.Once);

        // Should not publish success event
        _contextMock.Verify(c => c.Publish(
            It.IsAny<PrivacyErasureCompleted>(),
            CancellationToken.None), Times.Never);
    }

    [Fact]
    public async Task Consume_WhenDbUpdateConcurrencyException_ThrowsForRetry()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var message = new PrivacyErasureRequested
        {
            UserId = userId,
            RequestId = requestId
        };

        _contextMock
            .Setup(c => c.Message)
            .Returns(message);

        _contextMock
            .Setup(c => c.CancellationToken)
            .Returns(CancellationToken.None);

        var user = CreateUser("test@example.com", "testuser");
        user.Id = userId.ToString();

        _userManagerMock
            .Setup(u => u.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock
            .Setup(u => u.UpdateAsync(It.IsAny<User>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("Concurrency conflict"));

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => _consumer.Consume(_contextMock.Object));

        // Verify warning was logged
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Concurrency conflict")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_WhenProfileExistsButUserDoesnt_AnonymizesProfileOnly()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var message = new PrivacyErasureRequested
        {
            UserId = userId,
            RequestId = requestId
        };

        _contextMock
            .Setup(c => c.Message)
            .Returns(message);

        _contextMock
            .Setup(c => c.CancellationToken)
            .Returns(CancellationToken.None);

        _userManagerMock
            .Setup(u => u.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        var profile = CreateUserProfile(userId.ToString());
        _userProfileRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId.ToString(), CancellationToken.None))
            .ReturnsAsync(profile.Object);

        _contextMock
            .Setup(c => c.Publish(It.IsAny<PrivacyErasureCompleted>(), CancellationToken.None))
            .Returns(Task.CompletedTask);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        profile.Verify(p => p.UpdatePersonalInfo("DELETED", "DELETED", ""), Times.Once);
        profile.Verify(p => p.UpdateAddress("", "", "", "", ""), Times.Once);
        profile.Verify(p => p.UpdateProfileInfo(It.IsAny<string>(), ""), Times.Once);
        profile.Verify(p => p.SetAvatarUrl(""), Times.Once);

        _contextMock.Verify(c => c.Publish(
            It.Is<PrivacyErasureCompleted>(e =>
                e.RequestId == requestId &&
                e.UserId == userId &&
                e.ServiceName == "identity-svc"),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Consume_LogsProcessingStartAndCompletion()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var message = new PrivacyErasureRequested
        {
            UserId = userId,
            RequestId = requestId
        };

        _contextMock
            .Setup(c => c.Message)
            .Returns(message);

        _contextMock
            .Setup(c => c.CancellationToken)
            .Returns(CancellationToken.None);

        _userManagerMock
            .Setup(u => u.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        _userProfileRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId.ToString(), CancellationToken.None))
            .ReturnsAsync((UserProfile?)null);

        _contextMock
            .Setup(c => c.Publish(It.IsAny<PrivacyErasureCompleted>(), CancellationToken.None))
            .Returns(Task.CompletedTask);

        // Act
        await _consumer.Consume(_contextMock.Object);

        // Assert
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("GDPR erasure requested")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("anonymised for GDPR erasure")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static User CreateUser(string email, string username)
    {
        return new User
        {
            Email = email,
            UserName = username,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = username.ToUpperInvariant(),
            IsActive = true
        };
    }

    private static Mock<UserProfile> CreateUserProfile(string userId)
    {
        var profile = new Mock<UserProfile>();
        return profile;
    }
}