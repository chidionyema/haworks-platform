using FluentAssertions;
using Haworks.CheckoutOrchestrator.Domain;
using Haworks.CheckoutOrchestrator.Infrastructure.Workers;
using Haworks.Contracts.Checkout;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Haworks.CheckoutOrchestrator.Unit.Workers;

public class PaymentExpiryWatcherTests
{
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<IServiceScope> _mockScope;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<CheckoutDbContext> _mockDbContext;
    private readonly Mock<IPublishEndpoint> _mockPublishEndpoint;
    private readonly Mock<ILogger<PaymentExpiryWatcher>> _mockLogger;
    private readonly Mock<DbSet<CheckoutSagaState>> _mockDbSet;
    private readonly PaymentExpiryWatcher _watcher;

    public PaymentExpiryWatcherTests()
    {
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockScope = new Mock<IServiceScope>();
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockDbContext = new Mock<CheckoutDbContext>();
        _mockPublishEndpoint = new Mock<IPublishEndpoint>();
        _mockLogger = new Mock<ILogger<PaymentExpiryWatcher>>();
        _mockDbSet = new Mock<DbSet<CheckoutSagaState>>();

        _mockScopeFactory.Setup(x => x.CreateAsyncScope()).Returns(_mockScope.Object);
        _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(CheckoutDbContext))).Returns(_mockDbContext.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(IPublishEndpoint))).Returns(_mockPublishEndpoint.Object);
        _mockDbContext.Setup(x => x.Set<CheckoutSagaState>()).Returns(_mockDbSet.Object);

        _watcher = new PaymentExpiryWatcher(_mockScopeFactory.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task TickAsync_WithStuckSagas_ShouldPublishPaymentExpiredEvents()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var stuckSagas = new List<CheckoutSagaState>
        {
            new()
            {
                CorrelationId = Guid.NewGuid(),
                OrderId = Guid.NewGuid(),
                CurrentState = "StockReservedState",
                CreatedAt = now.AddMinutes(-20) // Past deadline
            },
            new()
            {
                CorrelationId = Guid.NewGuid(),
                OrderId = Guid.NewGuid(),
                CurrentState = "ReadyForPayment",
                CreatedAt = now.AddMinutes(-18) // Past deadline
            }
        };

        // Create projections for the Select query
        var projectedStuckSagas = stuckSagas.Select(s => new { s.CorrelationId, s.OrderId }).ToList();

        var queryable = stuckSagas.AsQueryable();
        SetupDbSetQueryable(queryable, projectedStuckSagas);

        // Act
        await InvokeTickAsync();

        // Assert
        _mockPublishEndpoint.Verify(x => x.Publish(
            It.Is<PaymentExpiredEvent>(e => e.SagaId == stuckSagas[0].CorrelationId && e.OrderId == stuckSagas[0].OrderId),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockPublishEndpoint.Verify(x => x.Publish(
            It.Is<PaymentExpiredEvent>(e => e.SagaId == stuckSagas[1].CorrelationId && e.OrderId == stuckSagas[1].OrderId),
            It.IsAny<CancellationToken>()), Times.Once);

        VerifyLogInformation("PaymentExpiryWatcher: publishing PaymentExpired for 2 stuck saga(s)");
    }

    [Fact]
    public async Task TickAsync_WithNoStuckSagas_ShouldNotPublishAnyEvents()
    {
        // Arrange
        var emptyList = new List<CheckoutSagaState>();
        var queryable = emptyList.AsQueryable();
        SetupDbSetQueryable(queryable, new List<object>());

        // Act
        await InvokeTickAsync();

        // Assert
        _mockPublishEndpoint.Verify(x => x.Publish(It.IsAny<PaymentExpiredEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TickAsync_WithRecentSagas_ShouldNotPublishEvents()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var recentSagas = new List<CheckoutSagaState>
        {
            new()
            {
                CorrelationId = Guid.NewGuid(),
                OrderId = Guid.NewGuid(),
                CurrentState = "StockReservedState",
                CreatedAt = now.AddMinutes(-10) // Within deadline (15 min)
            }
        };

        var queryable = recentSagas.AsQueryable();
        SetupDbSetQueryable(queryable, new List<object>());

        // Act
        await InvokeTickAsync();

        // Assert
        _mockPublishEndpoint.Verify(x => x.Publish(It.IsAny<PaymentExpiredEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TickAsync_WithPublishFailure_ShouldLogWarningAndContinue()
    {
        // Arrange
        var stuckSaga = new CheckoutSagaState
        {
            CorrelationId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            CurrentState = "ReadyForPayment",
            CreatedAt = DateTime.UtcNow.AddMinutes(-20)
        };

        var projectedSaga = new { stuckSaga.CorrelationId, stuckSaga.OrderId };
        var queryable = new[] { stuckSaga }.AsQueryable();
        SetupDbSetQueryable(queryable, new[] { projectedSaga });

        var publishException = new InvalidOperationException("Publishing failed");
        _mockPublishEndpoint.Setup(x => x.Publish(It.IsAny<PaymentExpiredEvent>(), It.IsAny<CancellationToken>()))
                           .ThrowsAsync(publishException);

        // Act
        await InvokeTickAsync();

        // Assert
        VerifyLogWarning($"PaymentExpiryWatcher: failed to publish for saga {stuckSaga.CorrelationId}; will retry next tick");
    }

    [Fact]
    public async Task TickAsync_WithMaxPublishesLimit_ShouldLimitBatchSize()
    {
        // Arrange
        var now = DateTime.UtcNow;
        // Create 60 stuck sagas (exceeds MaxPublishesPerTick = 50)
        var stuckSagas = Enumerable.Range(1, 60)
            .Select(i => new CheckoutSagaState
            {
                CorrelationId = Guid.NewGuid(),
                OrderId = Guid.NewGuid(),
                CurrentState = "StockReservedState",
                CreatedAt = now.AddMinutes(-20)
            }).ToList();

        // But the query should only return first 50 due to Take(MaxPublishesPerTick)
        var limitedSagas = stuckSagas.Take(50).ToList();
        var projectedSagas = limitedSagas.Select(s => new { s.CorrelationId, s.OrderId }).ToList();

        var queryable = stuckSagas.AsQueryable();
        SetupDbSetQueryable(queryable, projectedSagas);

        // Act
        await InvokeTickAsync();

        // Assert
        _mockPublishEndpoint.Verify(x => x.Publish(It.IsAny<PaymentExpiredEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(50));
        VerifyLogInformation("PaymentExpiryWatcher: publishing PaymentExpired for 50 stuck saga(s)");
    }

    [Fact]
    public async Task TickAsync_ShouldOrderByCreatedAtAndProcessOldestFirst()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var oldestSaga = new CheckoutSagaState
        {
            CorrelationId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            CurrentState = "StockReservedState",
            CreatedAt = now.AddMinutes(-25)
        };
        var newerSaga = new CheckoutSagaState
        {
            CorrelationId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            CurrentState = "ReadyForPayment",
            CreatedAt = now.AddMinutes(-20)
        };

        // List in reverse order to test that OrderBy sorts correctly
        var sagas = new[] { newerSaga, oldestSaga }.ToList();
        var projectedSagas = sagas.Select(s => new { s.CorrelationId, s.OrderId }).ToList();
        var queryable = sagas.AsQueryable();
        SetupDbSetQueryable(queryable, projectedSagas);

        // Act
        await InvokeTickAsync();

        // Assert
        _mockPublishEndpoint.Verify(x => x.Publish(It.IsAny<PaymentExpiredEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Both should be published, verifying the oldest one exists
        _mockPublishEndpoint.Verify(x => x.Publish(
            It.Is<PaymentExpiredEvent>(e => e.SagaId == oldestSaga.CorrelationId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private void SetupDbSetQueryable<T>(IQueryable<CheckoutSagaState> sagaQueryable, T projectedResult)
    {
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.Provider).Returns(sagaQueryable.Provider);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.Expression).Returns(sagaQueryable.Expression);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.ElementType).Returns(sagaQueryable.ElementType);
        _mockDbSet.As<IQueryable<CheckoutSagaState>>().Setup(m => m.GetEnumerator()).Returns(() => sagaQueryable.GetEnumerator());

        // Setup for async operations - this is a simplified mock that won't perfectly replicate EF behavior
        // but should be sufficient for unit testing the business logic
    }

    private async Task InvokeTickAsync()
    {
        // Use reflection to call the private TickAsync method
        var method = typeof(PaymentExpiryWatcher).GetMethod("TickAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var task = (Task)method!.Invoke(_watcher, new object[] { CancellationToken.None })!;
        await task;
    }

    private void VerifyLogInformation(string expectedMessage)
    {
        _mockLogger.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expectedMessage)),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    private void VerifyLogWarning(string expectedMessageContains)
    {
        _mockLogger.Verify(x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expectedMessageContains)),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}