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
    private readonly Mock<IUserProfileRepository> _userProfileRepoMock;
    private readonly Mock<ILogger<PrivacyErasureRequestedConsumer>> _loggerMock;
    private readonly PrivacyErasureRequestedConsumer _consumer;

    public PrivacyErasureRequestedConsumerTests(ITestOutputHelper output) : base(output)
    {
        var userStoreMock = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(
            userStoreMock.Object,
            null, null, null, null, null, null, null, null);

        _userProfileRepoMock = new Mock<IUserProfileRepository>();
        _loggerMock = new Mock<ILogger<PrivacyErasureRequestedConsumer>>();

        _consumer = new PrivacyErasureRequestedConsumer(
            _userManagerMock.Object,
            _userProfileRepoMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Consume_WithExistingUser_AnonymisesUserDataAndPublishesCompletion()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var @event = new PrivacyErasureRequested
        {
            UserId = userId,
            RequestId = requestId
        };

        var user = new User
        {
            Id = userId.ToString(),
            UserName = "testuser",
            Email = "test@example.com",
            PhoneNumber = "+1234567890",
            StripeCustomerId = "cus_123",
            CheckoutSessionId = "cs_123",
            IsActive = true
        };

        var userProfile = UserProfile.Create(userId.ToString(), "John", "Doe", "Bio", "website.com", "avatar.jpg");

        var contextMock = new Mock<ConsumeContext<PrivacyErasureRequested>>();
        contextMock.Setup(c => c.Message).Returns(@event);
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _userManagerMock.Setup(u => u.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        _userProfileRepoMock.Setup(r => r.GetByUserIdAsync(userId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(userProfile);

        var publishedEvents = new List<object>();
        contextMock.Setup(c => c.Publish<PrivacyErasureCompleted>(It.IsAny<PrivacyErasureCompleted>(), It.IsAny<CancellationToken>()))
            .Callback<PrivacyErasureCompleted, CancellationToken>((evt, ct) => publishedEvents.Add(evt))
            .Returns(Task.CompletedTask);

        // Act
        await _consumer.Consume(contextMock.Object);

        // Assert
        Assert.Equal($"DELETED-{userId:N}", user.UserName);
        Assert.Equal($"DELETED-{userId:N}".ToUpperInvariant(), user.NormalizedUserName);
        Assert.Equal($"deleted-{userId:N}@privacy.invalid", user.Email);
        Assert.Equal($"deleted-{userId:N}@privacy.invalid".ToUpperInvariant(), user.NormalizedEmail);
        Assert.Null(user.PhoneNumber);
        Assert.Null(user.StripeCustomerId);
        Assert.Null(user.CheckoutSessionId);
        Assert.False(user.IsActive);

        _userManagerMock.Verify(u => u.UpdateAsync(user), Times.Once);

        Assert.Single(publishedEvents);
        var completionEvent = Assert.IsType<PrivacyErasureCompleted>(publishedEvents.First());
        Assert.Equal(requestId, completionEvent.RequestId);
        Assert.Equal(userId, completionEvent.UserId);
        Assert.Equal("identity-svc", completionEvent.ServiceName);
    }

    [Fact]
    public async Task Consume_WithNonExistentUser_LogsAndPublishesCompletion()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var @event = new PrivacyErasureRequested
        {
            UserId = userId,
            RequestId = requestId
        };

        var contextMock = new Mock<ConsumeContext<PrivacyErasureRequested>>();
        contextMock.Setup(c => c.Message).Returns(@event);
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _userManagerMock.Setup(u => u.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        _userProfileRepoMock.Setup(r => r.GetByUserIdAsync(userId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserProfile?)null);

        var publishedEvents = new List<object>();
        contextMock.Setup(c => c.Publish<PrivacyErasureCompleted>(It.IsAny<PrivacyErasureCompleted>(), It.IsAny<CancellationToken>()))
            .Callback<PrivacyErasureCompleted, CancellationToken>((evt, ct) => publishedEvents.Add(evt))
            .Returns(Task.CompletedTask);

        // Act
        await _consumer.Consume(contextMock.Object);

        // Assert
        VerifyLogMessage(LogLevel.Information, $"User {userId} not found — already deleted or never existed");

        Assert.Single(publishedEvents);
        var completionEvent = Assert.IsType<PrivacyErasureCompleted>(publishedEvents.First());
        Assert.Equal(requestId, completionEvent.RequestId);
        Assert.Equal(userId, completionEvent.UserId);
        Assert.Equal("identity-svc", completionEvent.ServiceName);
    }

    [Fact]
    public async Task Consume_WhenUserUpdateFails_PublishesFailureEvent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var @event = new PrivacyErasureRequested
        {
            UserId = userId,
            RequestId = requestId
        };

        var user = new User
        {
            Id = userId.ToString(),
            UserName = "testuser",
            Email = "test@example.com"
        };

        var contextMock = new Mock<ConsumeContext<PrivacyErasureRequested>>();
        contextMock.Setup(c => c.Message).Returns(@event);
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _userManagerMock.Setup(u => u.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        var identityError = new IdentityError { Description = "Database constraint violation" };
        _userManagerMock.Setup(u => u.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Failed(identityError));

        var publishedEvents = new List<object>();
        contextMock.Setup(c => c.Publish<PrivacyErasureFailed>(It.IsAny<PrivacyErasureFailed>(), It.IsAny<CancellationToken>()))
            .Callback<PrivacyErasureFailed, CancellationToken>((evt, ct) => publishedEvents.Add(evt))
            .Returns(Task.CompletedTask);

        // Act
        await _consumer.Consume(contextMock.Object);

        // Assert
        Assert.Single(publishedEvents);
        var failureEvent = Assert.IsType<PrivacyErasureFailed>(publishedEvents.First());
        Assert.Equal(requestId, failureEvent.RequestId);
        Assert.Equal(userId, failureEvent.UserId);
        Assert.Equal("identity-svc", failureEvent.ServiceName);
        Assert.Contains("UserManager.UpdateAsync failed", failureEvent.ErrorMessage);
    }

    [Fact]
    public async Task Consume_WhenConcurrencyExceptionOccurs_PropagatesException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var @event = new PrivacyErasureRequested
        {
            UserId = userId,
            RequestId = requestId
        };

        var user = new User
        {
            Id = userId.ToString(),
            UserName = "testuser"
        };

        var contextMock = new Mock<ConsumeContext<PrivacyErasureRequested>>();
        contextMock.Setup(c => c.Message).Returns(@event);
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _userManagerMock.Setup(u => u.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _userManagerMock.Setup(u => u.UpdateAsync(user))
            .ThrowsAsync(new DbUpdateConcurrencyException("Concurrency conflict"));

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => _consumer.Consume(contextMock.Object));

        VerifyLogMessage(LogLevel.Warning, $"Concurrency conflict while anonymising user {userId}, will retry");
    }

    [Fact]
    public async Task Consume_WithExistingProfile_AnonymisesProfileData()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var @event = new PrivacyErasureRequested
        {
            UserId = userId,
            RequestId = requestId
        };

        var userProfile = UserProfile.Create(userId.ToString(), "John", "Doe", "My bio", "mywebsite.com", "avatar.jpg");
        userProfile.UpdateAddress("123 Main St", "Apt 1", "City", "State", "12345");

        var contextMock = new Mock<ConsumeContext<PrivacyErasureRequested>>();
        contextMock.Setup(c => c.Message).Returns(@event);
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        _userManagerMock.Setup(u => u.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        _userProfileRepoMock.Setup(r => r.GetByUserIdAsync(userId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(userProfile);

        contextMock.Setup(c => c.Publish<PrivacyErasureCompleted>(It.IsAny<PrivacyErasureCompleted>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _consumer.Consume(contextMock.Object);

        // Assert - profile should be anonymised
        Assert.Equal("DELETED", userProfile.FirstName);
        Assert.Equal("DELETED", userProfile.LastName);
        Assert.Equal("", userProfile.Bio);
        Assert.Equal("", userProfile.Website);
        Assert.Equal("", userProfile.AvatarUrl);
        Assert.Equal("", userProfile.Address1);
        Assert.Equal("", userProfile.Address2);
        Assert.Equal("", userProfile.City);
        Assert.Equal("", userProfile.State);
        Assert.Equal("", userProfile.ZipCode);
    }

    private void VerifyLogMessage(LogLevel level, string message)
    {
        _loggerMock.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(message)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}