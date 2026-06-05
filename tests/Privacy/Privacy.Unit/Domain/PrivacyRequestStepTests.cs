using FluentAssertions;
using Haworks.Privacy.Domain.Aggregates;
using Haworks.Privacy.Domain.Enums;
using Xunit;

namespace Haworks.Privacy.Unit.Domain;

public sealed class PrivacyRequestStepTests
{
    [Fact]
    public void Create_SetsCorrectInitialState()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var serviceName = "identity-svc";

        // Act
        var step = PrivacyRequestStep.Create(requestId, serviceName);

        // Assert
        step.RequestId.Should().Be(requestId);
        step.ServiceName.Should().Be(serviceName);
        step.Status.Should().Be(PrivacyRequestStatus.Pending);
        step.ErrorMessage.Should().BeNull();
        step.CompletedAt.Should().BeNull();
        step.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Complete_SetsStatusToCompletedWithTimestamp()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var serviceName = "orders-svc";
        var step = PrivacyRequestStep.Create(requestId, serviceName);
        var beforeComplete = DateTimeOffset.UtcNow;

        // Act
        step.Complete();

        // Assert
        var afterComplete = DateTimeOffset.UtcNow;
        step.Status.Should().Be(PrivacyRequestStatus.Completed);
        step.CompletedAt.Should().NotBeNull();
        step.CompletedAt!.Value.Should().BeCloseTo(beforeComplete, TimeSpan.FromSeconds(5));
        step.CompletedAt!.Value.Should().BeBefore(afterComplete.AddSeconds(1));
        step.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Fail_SetsStatusToFailedWithErrorMessage()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var serviceName = "payments-svc";
        var step = PrivacyRequestStep.Create(requestId, serviceName);
        var errorMessage = "Service timeout during user data deletion";

        // Act
        step.Fail(errorMessage);

        // Assert
        step.Status.Should().Be(PrivacyRequestStatus.Failed);
        step.ErrorMessage.Should().Be(errorMessage);
        step.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Complete_AfterFail_OverwritesFailedState()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var serviceName = "identity-svc";
        var step = PrivacyRequestStep.Create(requestId, serviceName);

        step.Fail("Initial failure");

        // Act
        step.Complete();

        // Assert
        step.Status.Should().Be(PrivacyRequestStatus.Completed);
        step.CompletedAt.Should().NotBeNull();
        // Note: ErrorMessage is preserved even after completion for audit trail
        step.ErrorMessage.Should().Be("Initial failure");
    }

    [Fact]
    public void Fail_AfterComplete_OverwritesCompletedState()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var serviceName = "orders-svc";
        var step = PrivacyRequestStep.Create(requestId, serviceName);

        step.Complete();
        var originalCompletedAt = step.CompletedAt;

        // Act
        step.Fail("Late failure detected");

        // Assert
        step.Status.Should().Be(PrivacyRequestStatus.Failed);
        step.ErrorMessage.Should().Be("Late failure detected");
        // CompletedAt should be cleared when marked as failed
        step.CompletedAt.Should().BeNull();
    }

    [Theory]
    [InlineData("identity-svc")]
    [InlineData("orders-svc")]
    [InlineData("payments-svc")]
    [InlineData("notifications-svc")]
    public void Create_WithDifferentServiceNames_SetsCorrectServiceName(string serviceName)
    {
        // Arrange
        var requestId = Guid.NewGuid();

        // Act
        var step = PrivacyRequestStep.Create(requestId, serviceName);

        // Assert
        step.ServiceName.Should().Be(serviceName);
        step.Status.Should().Be(PrivacyRequestStatus.Pending);
    }

    [Fact]
    public void Fail_WithEmptyMessage_StillSetsFailedStatus()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var serviceName = "identity-svc";
        var step = PrivacyRequestStep.Create(requestId, serviceName);

        // Act
        step.Fail("");

        // Assert
        step.Status.Should().Be(PrivacyRequestStatus.Failed);
        step.ErrorMessage.Should().Be("");
        step.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Fail_WithNullMessage_StillSetsFailedStatus()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var serviceName = "payments-svc";
        var step = PrivacyRequestStep.Create(requestId, serviceName);

        // Act
        step.Fail(null!);

        // Assert
        step.Status.Should().Be(PrivacyRequestStatus.Failed);
        step.ErrorMessage.Should().BeNull();
        step.CompletedAt.Should().BeNull();
    }
}