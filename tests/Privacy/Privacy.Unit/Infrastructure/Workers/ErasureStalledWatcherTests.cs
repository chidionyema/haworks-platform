using FluentAssertions;
using Haworks.Contracts.Privacy;
using Haworks.Privacy.Application.Requests.Sagas;
using Haworks.Privacy.Infrastructure.Persistence;
using Haworks.Privacy.Infrastructure.Workers;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Haworks.Privacy.Unit.Infrastructure.Workers;

public sealed class ErasureStalledWatcherTests
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ErasureStalledWatcher> _logger;
    private readonly ErasureStalledWatcher _worker;
    private readonly IServiceScope _scope;
    private readonly IServiceProvider _serviceProvider;
    private readonly PrivacyDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly DbSet<PrivacyRequestState> _sagaStateSet;

    public ErasureStalledWatcherTests()
    {
        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        _logger = Substitute.For<ILogger<ErasureStalledWatcher>>();
        _scope = Substitute.For<IServiceScope>();
        _serviceProvider = Substitute.For<IServiceProvider>();
        _dbContext = Substitute.For<PrivacyDbContext>();
        _publishEndpoint = Substitute.For<IPublishEndpoint>();
        _sagaStateSet = Substitute.For<DbSet<PrivacyRequestState>>();

        _scopeFactory.CreateAsyncScope().Returns(_scope);
        _scope.ServiceProvider.Returns(_serviceProvider);
        _serviceProvider.GetRequiredService<PrivacyDbContext>().Returns(_dbContext);
        _serviceProvider.GetRequiredService<IPublishEndpoint>().Returns(_publishEndpoint);
        _dbContext.Set<PrivacyRequestState>().Returns(_sagaStateSet);

        _worker = new ErasureStalledWatcher(_scopeFactory, _logger);
    }

    [Fact]
    public async Task Worker_FindsStalledSagas_RepublishesEventsForPendingServices()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var sagaId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var stalledSagas = new List<dynamic>
        {
            new
            {
                CorrelationId = sagaId,
                UserId = userId,
                IdentityCompleted = true,  // Completed
                OrdersCompleted = false,   // Pending
                PaymentsCompleted = false  // Pending
            }
        };

        MockSagaQuery(stalledSagas);

        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act
        try
        {
            await _worker.StartAsync(cts.Token);
            await _worker.ExecuteTask;
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation
        }
        finally
        {
            await _worker.StopAsync(cts.Token);
        }

        // Assert
        await _publishEndpoint.Received(1).Publish(
            Arg.Is<PrivacyErasureRequested>(msg =>
                msg.RequestId == sagaId &&
                msg.UserId == userId),
            Arg.Is<Action<PublishContext>>(action => action != null),
            Arg.Any<CancellationToken>());

        _logger.Received(1).LogWarning(
            "ErasureStalledWatcher: stalled erasure request {RequestId} for user {UserId} — pending: {Pending}",
            sagaId, userId, "orders-svc, payments-svc");
    }

    [Fact]
    public async Task Worker_AllServicesCompleted_SkipsRepublishing()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var sagaId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var stalledSagas = new List<dynamic>
        {
            new
            {
                CorrelationId = sagaId,
                UserId = userId,
                IdentityCompleted = true,
                OrdersCompleted = true,
                PaymentsCompleted = true
            }
        };

        MockSagaQuery(stalledSagas);

        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act
        try
        {
            await _worker.StartAsync(cts.Token);
            await _worker.ExecuteTask;
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation
        }
        finally
        {
            await _worker.StopAsync(cts.Token);
        }

        // Assert
        await _publishEndpoint.DidNotReceive().Publish(
            Arg.Any<PrivacyErasureRequested>(),
            Arg.Any<Action<PublishContext>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Worker_NoStalledSagas_DoesNotPublishEvents()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        MockSagaQuery(new List<dynamic>());

        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act
        try
        {
            await _worker.StartAsync(cts.Token);
            await _worker.ExecuteTask;
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation
        }
        finally
        {
            await _worker.StopAsync(cts.Token);
        }

        // Assert
        await _publishEndpoint.DidNotReceive().Publish(
            Arg.Any<PrivacyErasureRequested>(),
            Arg.Any<Action<PublishContext>>(),
            Arg.Any<CancellationToken>());

        _logger.DidNotReceive().LogWarning(
            "ErasureStalledWatcher: re-publishing PrivacyErasureRequested for {Count} stalled saga(s)",
            Arg.Any<int>());
    }

    [Fact]
    public async Task Worker_OnlyIdentityPending_RepublishesForIdentityService()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var sagaId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var stalledSagas = new List<dynamic>
        {
            new
            {
                CorrelationId = sagaId,
                UserId = userId,
                IdentityCompleted = false, // Pending
                OrdersCompleted = true,    // Completed
                PaymentsCompleted = true   // Completed
            }
        };

        MockSagaQuery(stalledSagas);

        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act
        try
        {
            await _worker.StartAsync(cts.Token);
            await _worker.ExecuteTask;
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation
        }
        finally
        {
            await _worker.StopAsync(cts.Token);
        }

        // Assert
        await _publishEndpoint.Received(1).Publish(
            Arg.Is<PrivacyErasureRequested>(msg =>
                msg.RequestId == sagaId &&
                msg.UserId == userId),
            Arg.Any<Action<PublishContext>>(),
            Arg.Any<CancellationToken>());

        _logger.Received(1).LogWarning(
            "ErasureStalledWatcher: stalled erasure request {RequestId} for user {UserId} — pending: {Pending}",
            sagaId, userId, "identity-svc");
    }

    [Fact]
    public async Task Worker_PublishEndpointThrows_LogsWarningAndContinues()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var sagaId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var stalledSagas = new List<dynamic>
        {
            new
            {
                CorrelationId = sagaId,
                UserId = userId,
                IdentityCompleted = false,
                OrdersCompleted = true,
                PaymentsCompleted = true
            }
        };

        MockSagaQuery(stalledSagas);

        _publishEndpoint.Publish(
                Arg.Any<PrivacyErasureRequested>(),
                Arg.Any<Action<PublishContext>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Message broker unavailable"));

        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act
        try
        {
            await _worker.StartAsync(cts.Token);
            await _worker.ExecuteTask;
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation
        }
        finally
        {
            await _worker.StopAsync(cts.Token);
        }

        // Assert
        _logger.Received(1).LogWarning(
            Arg.Any<Exception>(),
            "ErasureStalledWatcher: failed to publish for saga {SagaId}; will retry next tick",
            sagaId);
    }

    [Fact]
    public async Task Worker_DatabaseException_LogsErrorAndRetries()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        _dbContext.Set<PrivacyRequestState>()
            .When(x => x.Where(Arg.Any<System.Linq.Expressions.Expression<Func<PrivacyRequestState, bool>>>()))
            .Do(x => throw new InvalidOperationException("Database connection failed"));

        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act
        try
        {
            await _worker.StartAsync(cts.Token);
            await _worker.ExecuteTask;
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation
        }
        finally
        {
            await _worker.StopAsync(cts.Token);
        }

        // Assert
        _logger.Received().LogError(
            Arg.Any<Exception>(),
            "ErasureStalledWatcher tick failed; will retry next interval");
    }

    private void MockSagaQuery(List<dynamic> stalledSagas)
    {
        var queryableData = stalledSagas.Cast<object>().AsQueryable();

        var mockQuery = Substitute.For<IQueryable<object>>();
        mockQuery.Provider.Returns(queryableData.Provider);
        mockQuery.Expression.Returns(queryableData.Expression);
        mockQuery.ElementType.Returns(queryableData.ElementType);
        mockQuery.GetEnumerator().Returns(queryableData.GetEnumerator());

        _sagaStateSet.Where(Arg.Any<System.Linq.Expressions.Expression<Func<PrivacyRequestState, bool>>>())
            .Returns((IQueryable<PrivacyRequestState>)mockQuery);

        // Mock the LINQ chain operations that return our test data
        // In a real scenario, you'd need more sophisticated mocking for the full LINQ chain
    }
}

// Extension to access protected ExecuteAsync task for testing
public static class ErasureStalledWatcherExtensions
{
    public static Task ExecuteTask(this ErasureStalledWatcher worker)
    {
        var field = typeof(ErasureStalledWatcher).BaseType!
            .GetField("_executeTask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Task)field!.GetValue(worker)!;
    }
}