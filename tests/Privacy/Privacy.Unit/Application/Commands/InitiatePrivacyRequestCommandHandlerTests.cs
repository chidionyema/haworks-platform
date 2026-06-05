using FluentAssertions;
using Haworks.Privacy.Application.Common.Interfaces;
using Haworks.Privacy.Application.Requests.Commands.InitiateRequest;
using Haworks.Privacy.Domain.Aggregates;
using Haworks.Privacy.Domain.Enums;
using Haworks.Contracts.Privacy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NSubstitute;
using Npgsql;
using Xunit;

namespace Haworks.Privacy.Unit.Application.Commands;

public sealed class InitiatePrivacyRequestCommandHandlerTests
{
    private readonly IPrivacyDbContext _context;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly InitiatePrivacyRequestCommandHandler _handler;
    private readonly DatabaseFacade _database;
    private readonly DbSet<PrivacyRequest> _privacyRequests;

    public InitiatePrivacyRequestCommandHandlerTests()
    {
        _context = Substitute.For<IPrivacyDbContext>();
        _publishEndpoint = Substitute.For<IPublishEndpoint>();
        _database = Substitute.For<DatabaseFacade>();
        _privacyRequests = Substitute.For<DbSet<PrivacyRequest>>();

        _context.Database.Returns(_database);
        _context.PrivacyRequests.Returns(_privacyRequests);

        _handler = new InitiatePrivacyRequestCommandHandler(_context, _publishEndpoint);
    }

    [Fact]
    public async Task Handle_NoExistingRequest_CreatesNewRequestAndPublishesEvent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new InitiatePrivacyRequestCommand(userId, PrivacyRequestType.Erasure);
        var ct = CancellationToken.None;

        _privacyRequests.FirstOrDefaultAsync(
                Arg.Any<System.Linq.Expressions.Expression<Func<PrivacyRequest, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns((PrivacyRequest?)null);

        // Act
        var result = await _handler.Handle(command, ct);

        // Assert
        result.Should().NotBeEmpty();

        // Verify the advisory lock query was called with correct parameters
        await _database.Received(1).ExecuteSqlInterpolatedAsync(
            Arg.Is<FormattableString>(sql =>
                sql.ToString().Contains("FOR UPDATE SKIP LOCKED") &&
                sql.ToString().Contains("privacy.\"PrivacyRequests\"") &&
                sql.ToString().Contains("Status")),
            ct);

        // Verify entity was added
        _context.PrivacyRequests.Received(1).Add(Arg.Is<PrivacyRequest>(pr =>
            pr.UserId == userId &&
            pr.Type == PrivacyRequestType.Erasure &&
            pr.Status == PrivacyRequestStatus.Pending));

        // Verify event was published before SaveChanges
        await _publishEndpoint.Received(1).Publish(
            Arg.Is<InitiatePrivacyRequestMessage>(msg =>
                msg.UserId == userId), ct);

        await _context.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Handle_ExistingActiveRequest_ReturnsExistingId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var existingRequestId = Guid.NewGuid();
        var command = new InitiatePrivacyRequestCommand(userId, PrivacyRequestType.Erasure);
        var ct = CancellationToken.None;

        var existingRequest = PrivacyRequest.Create(userId, PrivacyRequestType.Erasure);
        typeof(PrivacyRequest).GetProperty("Id")!.SetValue(existingRequest, existingRequestId);

        _privacyRequests.FirstOrDefaultAsync(
                Arg.Any<System.Linq.Expressions.Expression<Func<PrivacyRequest, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(existingRequest);

        // Act
        var result = await _handler.Handle(command, ct);

        // Assert
        result.Should().Be(existingRequestId);

        // Verify no new entity was added
        _context.PrivacyRequests.DidNotReceive().Add(Arg.Any<PrivacyRequest>());

        // Verify no event was published
        await _publishEndpoint.DidNotReceive().Publish(Arg.Any<InitiatePrivacyRequestMessage>(), ct);

        // Verify SaveChanges was not called
        await _context.DidNotReceive().SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Handle_ConcurrentInsertException_RetriesAndReturnsExisting()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var existingRequestId = Guid.NewGuid();
        var command = new InitiatePrivacyRequestCommand(userId, PrivacyRequestType.Erasure);
        var ct = CancellationToken.None;

        // First query returns null (no existing request)
        _privacyRequests.FirstOrDefaultAsync(
                Arg.Any<System.Linq.Expressions.Expression<Func<PrivacyRequest, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns((PrivacyRequest?)null,
                     PrivacyRequest.Create(userId, PrivacyRequestType.Erasure));

        // SaveChanges throws unique constraint violation (23505)
        var postgresException = new PostgresException("", "", "", "23505");
        var dbUpdateException = new DbUpdateException("Duplicate key", postgresException);
        _context.SaveChangesAsync(ct).ThrowsForAnyArgs(dbUpdateException);

        // Act
        var result = await _handler.Handle(command, ct);

        // Assert
        result.Should().NotBeEmpty();

        // Verify the advisory lock was called
        await _database.Received(1).ExecuteSqlInterpolatedAsync(
            Arg.Any<FormattableString>(), ct);

        // Verify entity was added (before exception)
        _context.PrivacyRequests.Received(1).Add(Arg.Any<PrivacyRequest>());

        // Verify event was published (before exception)
        await _publishEndpoint.Received(1).Publish(Arg.Any<InitiatePrivacyRequestMessage>(), ct);

        // Verify SaveChanges was attempted
        await _context.Received(1).SaveChangesAsync(ct);

        // Verify retry query was executed
        _privacyRequests.Received(2).FirstOrDefaultAsync(
            Arg.Any<System.Linq.Expressions.Expression<Func<PrivacyRequest, bool>>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ConcurrentInsertExceptionButNoExistingRecord_RethrowsException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new InitiatePrivacyRequestCommand(userId, PrivacyRequestType.Erasure);
        var ct = CancellationToken.None;

        // Both queries return null
        _privacyRequests.FirstOrDefaultAsync(
                Arg.Any<System.Linq.Expressions.Expression<Func<PrivacyRequest, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns((PrivacyRequest?)null);

        // SaveChanges throws unique constraint violation
        var postgresException = new PostgresException("", "", "", "23505");
        var dbUpdateException = new DbUpdateException("Duplicate key", postgresException);
        _context.SaveChangesAsync(ct).ThrowsForAnyArgs(dbUpdateException);

        // Act & Assert
        var act = () => _handler.Handle(command, ct);

        await act.Should().ThrowAsync<DbUpdateException>()
            .Where(ex => ex.InnerException is PostgresException);
    }

    [Fact]
    public async Task Handle_NonConcurrencyDbUpdateException_RethrowsException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new InitiatePrivacyRequestCommand(userId, PrivacyRequestType.Erasure);
        var ct = CancellationToken.None;

        _privacyRequests.FirstOrDefaultAsync(
                Arg.Any<System.Linq.Expressions.Expression<Func<PrivacyRequest, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns((PrivacyRequest?)null);

        // SaveChanges throws different exception (not 23505)
        var postgresException = new PostgresException("", "", "", "22001"); // string too long
        var dbUpdateException = new DbUpdateException("Other error", postgresException);
        _context.SaveChangesAsync(ct).ThrowsForAnyArgs(dbUpdateException);

        // Act & Assert
        var act = () => _handler.Handle(command, ct);

        await act.Should().ThrowAsync<DbUpdateException>()
            .Where(ex => ex.InnerException is PostgresException p && p.SqlState == "22001");
    }
}