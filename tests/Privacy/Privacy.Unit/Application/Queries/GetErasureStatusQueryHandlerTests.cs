using FluentAssertions;
using Haworks.Privacy.Application.Common.Interfaces;
using Haworks.Privacy.Application.Requests.Queries.GetErasureStatus;
using Haworks.Privacy.Application.Requests.Sagas;
using Haworks.Privacy.Domain.Enums;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Haworks.Privacy.Unit.Application.Queries;

public sealed class GetErasureStatusQueryHandlerTests
{
    private readonly ISagaStateRepository _sagaRepository;
    private readonly ILogger<GetErasureStatusQueryHandler> _logger;
    private readonly GetErasureStatusQueryHandler _handler;

    public GetErasureStatusQueryHandlerTests()
    {
        _sagaRepository = Substitute.For<ISagaStateRepository>();
        _logger = Substitute.For<ILogger<GetErasureStatusQueryHandler>>();
        _handler = new GetErasureStatusQueryHandler(_sagaRepository, _logger);
    }

    [Fact]
    public async Task Handle_NoSagaFound_ReturnsNull()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var query = new GetErasureStatusQuery(requestId, userId);
        var ct = CancellationToken.None;

        _sagaRepository.FindAsync(requestId, ct).Returns((PrivacyRequestState?)null);

        // Act
        var result = await _handler.Handle(query, ct);

        // Assert
        result.Should().BeNull();

        _logger.Received(1).LogDebug(
            "No saga state found for request {RequestId}",
            requestId);
    }

    [Fact]
    public async Task Handle_SagaBelongsToDifferentUser_ReturnsNullAndLogsWarning()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var callerUserId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid(); // Different user
        var query = new GetErasureStatusQuery(requestId, callerUserId);
        var ct = CancellationToken.None;

        var saga = CreateSagaState(requestId, ownerUserId, "Processing");
        _sagaRepository.FindAsync(requestId, ct).Returns(saga);

        // Act
        var result = await _handler.Handle(query, ct);

        // Assert
        result.Should().BeNull();

        _logger.Received(1).LogWarning(
            "GetErasureStatus: requestId {RequestId} belongs to user {OwnerId}, not caller {CallerId}",
            requestId, ownerUserId, callerUserId);
    }

    [Fact]
    public async Task Handle_ValidSagaInProcessingState_ReturnsMappedDto()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var query = new GetErasureStatusQuery(requestId, userId);
        var ct = CancellationToken.None;

        var saga = CreateSagaState(requestId, userId, "Processing");
        saga.IdentityCompleted = true;
        saga.OrdersCompleted = false;
        saga.PaymentsCompleted = true;

        _sagaRepository.FindAsync(requestId, ct).Returns(saga);

        // Act
        var result = await _handler.Handle(query, ct);

        // Assert
        result.Should().NotBeNull();
        result!.RequestId.Should().Be(requestId);
        result.UserId.Should().Be(userId);
        result.Type.Should().Be(PrivacyRequestType.Erasure);
        result.Status.Should().Be(PrivacyRequestStatus.InProgress);
        result.IdentityErased.Should().BeTrue();
        result.OrdersErased.Should().BeFalse();
        result.PaymentsErased.Should().BeTrue();
        result.FailedServices.Should().BeNull();
        result.CreatedAt.Should().Be(saga.CreatedAt!.Value);
        result.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_SagaInStalledState_MapsToInProgress()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var query = new GetErasureStatusQuery(requestId, userId);
        var ct = CancellationToken.None;

        var saga = CreateSagaState(requestId, userId, "Stalled");
        saga.FailedServices = "orders-svc,payments-svc";

        _sagaRepository.FindAsync(requestId, ct).Returns(saga);

        // Act
        var result = await _handler.Handle(query, ct);

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be(PrivacyRequestStatus.InProgress);
        result.FailedServices.Should().Be("orders-svc,payments-svc");
    }

    [Fact]
    public async Task Handle_SagaInCompletedState_ReturnsCompletedStatus()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var query = new GetErasureStatusQuery(requestId, userId);
        var ct = CancellationToken.None;

        var completedAt = DateTime.UtcNow.AddHours(-1);
        var saga = CreateSagaState(requestId, userId, "Completed");
        saga.CompletedAt = completedAt;
        saga.IdentityCompleted = true;
        saga.OrdersCompleted = true;
        saga.PaymentsCompleted = true;

        _sagaRepository.FindAsync(requestId, ct).Returns(saga);

        // Act
        var result = await _handler.Handle(query, ct);

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be(PrivacyRequestStatus.Completed);
        result.CompletedAt.Should().Be(new DateTimeOffset(completedAt, TimeSpan.Zero));
        result.IdentityErased.Should().BeTrue();
        result.OrdersErased.Should().BeTrue();
        result.PaymentsErased.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_SagaInFailedState_ReturnsFailedStatus()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var query = new GetErasureStatusQuery(requestId, userId);
        var ct = CancellationToken.None;

        var saga = CreateSagaState(requestId, userId, "Failed");
        saga.FailedServices = "identity-svc";

        _sagaRepository.FindAsync(requestId, ct).Returns(saga);

        // Act
        var result = await _handler.Handle(query, ct);

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be(PrivacyRequestStatus.Failed);
        result.FailedServices.Should().Be("identity-svc");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("Initial")]
    [InlineData(null)]
    public async Task Handle_SagaInUnknownState_MapsToPending(string? unknownState)
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var query = new GetErasureStatusQuery(requestId, userId);
        var ct = CancellationToken.None;

        var saga = CreateSagaState(requestId, userId, unknownState ?? "");

        _sagaRepository.FindAsync(requestId, ct).Returns(saga);

        // Act
        var result = await _handler.Handle(query, ct);

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be(PrivacyRequestStatus.Pending);
    }

    [Fact]
    public async Task Handle_SagaWithNullCreatedAt_UsesMinDateTime()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var query = new GetErasureStatusQuery(requestId, userId);
        var ct = CancellationToken.None;

        var saga = CreateSagaState(requestId, userId, "Processing");
        saga.CreatedAt = null; // Edge case: null created date

        _sagaRepository.FindAsync(requestId, ct).Returns(saga);

        // Act
        var result = await _handler.Handle(query, ct);

        // Assert
        result.Should().NotBeNull();
        result!.CreatedAt.Should().Be(DateTime.MinValue);
    }

    private static PrivacyRequestState CreateSagaState(Guid requestId, Guid userId, string currentState)
    {
        return new PrivacyRequestState
        {
            CorrelationId = requestId,
            UserId = userId,
            CurrentState = currentState,
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            IdentityCompleted = false,
            OrdersCompleted = false,
            PaymentsCompleted = false,
            FailedServices = null,
            CompletedAt = null
        };
    }
}