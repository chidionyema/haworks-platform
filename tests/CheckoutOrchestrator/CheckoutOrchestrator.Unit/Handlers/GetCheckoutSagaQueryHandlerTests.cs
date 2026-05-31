using FluentAssertions;
using Haworks.CheckoutOrchestrator.Application.Interfaces;
using Haworks.CheckoutOrchestrator.Application.Queries;
using Haworks.CheckoutOrchestrator.Application.QueryHandlers;
using Haworks.CheckoutOrchestrator.Domain;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Haworks.CheckoutOrchestrator.Unit.Handlers;

public class GetCheckoutSagaQueryHandlerTests
{
    private readonly Mock<ICheckoutDbContext> _mockDbContext;
    private readonly Mock<DbSet<CheckoutSagaState>> _mockDbSet;
    private readonly GetCheckoutSagaQueryHandler _handler;

    public GetCheckoutSagaQueryHandlerTests()
    {
        _mockDbContext = new Mock<ICheckoutDbContext>();
        _mockDbSet = new Mock<DbSet<CheckoutSagaState>>();

        _mockDbContext.Setup(x => x.CheckoutSagas).Returns(_mockDbSet.Object);
        _handler = new GetCheckoutSagaQueryHandler(_mockDbContext.Object);
    }

    [Fact]
    public async Task Handle_WithExistingSagaAsAdmin_ShouldReturnSuccess()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var saga = new CheckoutSagaState
        {
            CorrelationId = sagaId,
            OrderId = orderId,
            UserId = "user-123",
            CurrentState = "Initiated",
            PaymentId = Guid.NewGuid(),
            PaymentCheckoutUrl = "https://example.com/checkout",
            CreatedAt = DateTime.UtcNow
        };

        var queryable = new[] { saga }.AsQueryable();
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.Provider).Returns(queryable.Provider);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.Expression).Returns(queryable.Expression);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.GetEnumerator()).Returns(() => queryable.GetEnumerator());

        var query = new GetCheckoutSagaQuery(sagaId, "different-user", IsAdmin: true);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SagaId.Should().Be(sagaId);
        result.Value.OrderId.Should().Be(orderId);
        result.Value.CurrentState.Should().Be("Initiated");
        result.Value.PaymentId.Should().Be(saga.PaymentId);
        result.Value.PaymentCheckoutUrl.Should().Be("https://example.com/checkout");
    }

    [Fact]
    public async Task Handle_WithExistingSagaAsOwner_ShouldReturnSuccess()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var userId = "user-123";
        var saga = new CheckoutSagaState
        {
            CorrelationId = sagaId,
            OrderId = Guid.NewGuid(),
            UserId = userId,
            CurrentState = "PaymentConfirmed",
            CreatedAt = DateTime.UtcNow
        };

        var queryable = new[] { saga }.AsQueryable();
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.Provider).Returns(queryable.Provider);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.Expression).Returns(queryable.Expression);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.GetEnumerator()).Returns(() => queryable.GetEnumerator());

        var query = new GetCheckoutSagaQuery(sagaId, userId, IsAdmin: false);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SagaId.Should().Be(sagaId);
        result.Value.CurrentState.Should().Be("PaymentConfirmed");
    }

    [Fact]
    public async Task Handle_WithNonExistentSaga_ShouldReturnNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var emptyQueryable = Enumerable.Empty<CheckoutSagaState>().AsQueryable();

        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.Provider).Returns(emptyQueryable.Provider);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.Expression).Returns(emptyQueryable.Expression);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.ElementType).Returns(emptyQueryable.ElementType);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.GetEnumerator()).Returns(() => emptyQueryable.GetEnumerator());

        var query = new GetCheckoutSagaQuery(nonExistentId, "user-123", IsAdmin: true);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CheckoutSaga.NotFound");
        result.Error.Message.Should().Be("Saga not found.");
    }

    [Fact]
    public async Task Handle_WithDifferentUserAsNonAdmin_ShouldReturnForbidden()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var saga = new CheckoutSagaState
        {
            CorrelationId = sagaId,
            OrderId = Guid.NewGuid(),
            UserId = "original-user",
            CurrentState = "Initiated",
            CreatedAt = DateTime.UtcNow
        };

        var queryable = new[] { saga }.AsQueryable();
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.Provider).Returns(queryable.Provider);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.Expression).Returns(queryable.Expression);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.GetEnumerator()).Returns(() => queryable.GetEnumerator());

        var query = new GetCheckoutSagaQuery(sagaId, "different-user", IsAdmin: false);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CheckoutSaga.Forbidden");
        result.Error.Message.Should().Be("You are not authorized to view this saga.");
    }

    [Fact]
    public async Task Handle_WithNullUserIdAsNonAdmin_ShouldReturnForbidden()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var saga = new CheckoutSagaState
        {
            CorrelationId = sagaId,
            OrderId = Guid.NewGuid(),
            UserId = "user-123",
            CurrentState = "Initiated",
            CreatedAt = DateTime.UtcNow
        };

        var queryable = new[] { saga }.AsQueryable();
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.Provider).Returns(queryable.Provider);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.Expression).Returns(queryable.Expression);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.GetEnumerator()).Returns(() => queryable.GetEnumerator());

        var query = new GetCheckoutSagaQuery(sagaId, UserId: null, IsAdmin: false);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CheckoutSaga.Forbidden");
    }
}

public class GetCheckoutSagaByOrderIdQueryHandlerTests
{
    private readonly Mock<ICheckoutDbContext> _mockDbContext;
    private readonly Mock<DbSet<CheckoutSagaState>> _mockDbSet;
    private readonly GetCheckoutSagaByOrderIdQueryHandler _handler;

    public GetCheckoutSagaByOrderIdQueryHandlerTests()
    {
        _mockDbContext = new Mock<ICheckoutDbContext>();
        _mockDbSet = new Mock<DbSet<CheckoutSagaState>>();

        _mockDbContext.Setup(x => x.CheckoutSagas).Returns(_mockDbSet.Object);
        _handler = new GetCheckoutSagaByOrderIdQueryHandler(_mockDbContext.Object);
    }

    [Fact]
    public async Task Handle_WithExistingOrderIdAsAdmin_ShouldReturnSuccess()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var saga = new CheckoutSagaState
        {
            CorrelationId = Guid.NewGuid(),
            OrderId = orderId,
            UserId = "user-123",
            CurrentState = "Completed",
            CreatedAt = DateTime.UtcNow,
            FailureReason = null
        };

        var queryable = new[] { saga }.AsQueryable();
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.Provider).Returns(queryable.Provider);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.Expression).Returns(queryable.Expression);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.GetEnumerator()).Returns(() => queryable.GetEnumerator());

        var query = new GetCheckoutSagaByOrderIdQuery(orderId, "different-user", IsAdmin: true);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.OrderId.Should().Be(orderId);
        result.Value.CurrentState.Should().Be("Completed");
        result.Value.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithNonExistentOrderId_ShouldReturnNotFound()
    {
        // Arrange
        var nonExistentOrderId = Guid.NewGuid();
        var emptyQueryable = Enumerable.Empty<CheckoutSagaState>().AsQueryable();

        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.Provider).Returns(emptyQueryable.Provider);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.Expression).Returns(emptyQueryable.Expression);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.ElementType).Returns(emptyQueryable.ElementType);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.GetEnumerator()).Returns(() => emptyQueryable.GetEnumerator());

        var query = new GetCheckoutSagaByOrderIdQuery(nonExistentOrderId, "user-123", IsAdmin: true);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CheckoutSaga.NotFound");
    }

    [Fact]
    public async Task Handle_WithUnauthorizedUser_ShouldReturnForbidden()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var saga = new CheckoutSagaState
        {
            CorrelationId = Guid.NewGuid(),
            OrderId = orderId,
            UserId = "original-user",
            CurrentState = "Failed",
            FailureReason = "Payment declined",
            CreatedAt = DateTime.UtcNow
        };

        var queryable = new[] { saga }.AsQueryable();
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.Provider).Returns(queryable.Provider);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.Expression).Returns(queryable.Expression);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.GetEnumerator()).Returns(() => queryable.GetEnumerator());

        var query = new GetCheckoutSagaByOrderIdQuery(orderId, "unauthorized-user", IsAdmin: false);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CheckoutSaga.Forbidden");
        result.Error.Message.Should().Be("You are not authorized to view this saga.");
    }
}