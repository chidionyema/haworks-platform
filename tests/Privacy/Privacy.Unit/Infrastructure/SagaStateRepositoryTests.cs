using FluentAssertions;
using Haworks.Privacy.Application.Requests.Sagas;
using Haworks.Privacy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Haworks.Privacy.Unit.Infrastructure;

public sealed class SagaStateRepositoryTests
{
    private readonly PrivacyDbContext _dbContext;
    private readonly SagaStateRepository _repository;
    private readonly DbSet<PrivacyRequestState> _sagaStateSet;

    public SagaStateRepositoryTests()
    {
        _dbContext = Substitute.For<PrivacyDbContext>();
        _sagaStateSet = Substitute.For<DbSet<PrivacyRequestState>>();
        _dbContext.Set<PrivacyRequestState>().Returns(_sagaStateSet);

        _repository = new SagaStateRepository(_dbContext);
    }

    [Fact]
    public async Task FindAsync_ExistingSaga_ReturnsSagaState()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ct = CancellationToken.None;

        var expectedSaga = new PrivacyRequestState
        {
            CorrelationId = requestId,
            UserId = userId,
            CurrentState = "Processing"
        };

        _sagaStateSet.AsNoTracking().Returns(_sagaStateSet);
        _sagaStateSet.FirstOrDefaultAsync(
                Arg.Any<System.Linq.Expressions.Expression<Func<PrivacyRequestState, bool>>>(),
                ct)
            .Returns(expectedSaga);

        // Act
        var result = await _repository.FindAsync(requestId, ct);

        // Assert
        result.Should().Be(expectedSaga);

        // Verify AsNoTracking was called for read-only query optimization
        _sagaStateSet.Received(1).AsNoTracking();

        // Verify the correct predicate was used to find by CorrelationId
        await _sagaStateSet.Received(1).FirstOrDefaultAsync(
            Arg.Any<System.Linq.Expressions.Expression<Func<PrivacyRequestState, bool>>>(),
            ct);
    }

    [Fact]
    public async Task FindAsync_NonExistentSaga_ReturnsNull()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var ct = CancellationToken.None;

        _sagaStateSet.AsNoTracking().Returns(_sagaStateSet);
        _sagaStateSet.FirstOrDefaultAsync(
                Arg.Any<System.Linq.Expressions.Expression<Func<PrivacyRequestState, bool>>>(),
                ct)
            .Returns((PrivacyRequestState?)null);

        // Act
        var result = await _repository.FindAsync(requestId, ct);

        // Assert
        result.Should().BeNull();

        _sagaStateSet.Received(1).AsNoTracking();
    }

    [Fact]
    public async Task FindAsync_WithCancellation_PropagatesToken()
    {
        // Arrange
        var requestId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var ct = cts.Token;

        _sagaStateSet.AsNoTracking().Returns(_sagaStateSet);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _repository.FindAsync(requestId, ct));
    }

    [Fact]
    public async Task FindAsync_DefaultCancellationToken_UsesDefault()
    {
        // Arrange
        var requestId = Guid.NewGuid();

        _sagaStateSet.AsNoTracking().Returns(_sagaStateSet);
        _sagaStateSet.FirstOrDefaultAsync(
                Arg.Any<System.Linq.Expressions.Expression<Func<PrivacyRequestState, bool>>>(),
                default)
            .Returns((PrivacyRequestState?)null);

        // Act
        var result = await _repository.FindAsync(requestId); // No cancellation token provided

        // Assert
        result.Should().BeNull();

        // Verify default cancellation token was used
        await _sagaStateSet.Received(1).FirstOrDefaultAsync(
            Arg.Any<System.Linq.Expressions.Expression<Func<PrivacyRequestState, bool>>>(),
            default);
    }
}