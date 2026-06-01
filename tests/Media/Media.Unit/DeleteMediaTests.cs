using FluentAssertions;
using Haworks.BuildingBlocks.CurrentUser;
using Haworks.Contracts.Media;
using Haworks.Media.Api.Application;
using Haworks.Media.Api.Domain;
using Haworks.Media.Api.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Haworks.Media.Unit;

public class DeleteMediaTests
{
    private readonly MediaDbContext _context;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly Mock<IPublishEndpoint> _publisherMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly DeleteMediaHandler _handler;
    private const string OwnerId = "test-owner-123";

    public DeleteMediaTests()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _context = new MediaDbContext(options);
        _currentUserMock = new Mock<ICurrentUserService>();
        _publisherMock = new Mock<IPublishEndpoint>();
        _timeProviderMock = new Mock<TimeProvider>();
        _currentUserMock.Setup(x => x.UserId).Returns(OwnerId);

        var fixedTime = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(fixedTime);

        _handler = new DeleteMediaHandler(_context, _currentUserMock.Object, _publisherMock.Object, _timeProviderMock.Object);
    }

    [Fact]
    public async Task Handle_EmptyUserId_ReturnsUnauthorized()
    {
        _currentUserMock.Setup(x => x.UserId).Returns((string)null!);
        var command = new DeleteMediaCommand(Guid.NewGuid());

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Media.Unauthorized");
        result.Error.Message.Should().Be("Authenticated user identity could not be resolved.");
    }

    [Fact]
    public async Task Handle_MediaNotFound_ReturnsNotFound()
    {
        var command = new DeleteMediaCommand(Guid.NewGuid());

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Media.NotFound");
        result.Error.Message.Should().Be("Media file not found.");
    }

    [Fact]
    public async Task Handle_DifferentOwner_ReturnsForbidden()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "different-owner-id");
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var command = new DeleteMediaCommand(file.Id);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Media.Forbidden");
        result.Error.Message.Should().Be("You do not own this media file.");
    }

    [Fact]
    public async Task Handle_ValidFile_MarksAsDeletedAndPublishesEvent()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);
        file.MarkAsActive();
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var command = new DeleteMediaCommand(file.Id);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Verify file is marked as deleted
        var deletedFile = await _context.MediaFiles.FindAsync(file.Id);
        deletedFile.Should().NotBeNull();
        deletedFile!.IsDeleted.Should().BeTrue();
        deletedFile.DeletedAt.Should().NotBeNull();
        deletedFile.Status.Should().Be(MediaStatus.Deleted);

        // Verify event was published
        _publisherMock.Verify(x => x.Publish(
            It.Is<MediaDeletedEvent>(e =>
                e.MediaId == file.Id &&
                e.OwnerId == OwnerId &&
                e.EntityId == null &&
                e.EntityType == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_FileWithEntityLink_PublishesEventWithEntityInfo()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);
        var entityId = Guid.NewGuid();
        file.SetEntityLink(entityId, "Product");
        file.MarkAsActive();
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var command = new DeleteMediaCommand(file.Id);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Verify event includes entity information
        _publisherMock.Verify(x => x.Publish(
            It.Is<MediaDeletedEvent>(e =>
                e.MediaId == file.Id &&
                e.OwnerId == OwnerId &&
                e.EntityId == entityId &&
                e.EntityType == "Product"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidFile_UsesTransactionForAtomicity()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var command = new DeleteMediaCommand(file.Id);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Verify both database update and event publishing succeeded
        var deletedFile = await _context.MediaFiles.FindAsync(file.Id);
        deletedFile!.IsDeleted.Should().BeTrue();

        _publisherMock.Verify(x => x.Publish(It.IsAny<MediaDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyDeletedFile_StillSucceeds()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);
        file.MarkAsActive();
        var timeProvider = TimeProvider.System;
        file.MarkAsDeleted(timeProvider);
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var command = new DeleteMediaCommand(file.Id);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Should still publish event for idempotency
        _publisherMock.Verify(x => x.Publish(It.IsAny<MediaDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(MediaStatus.Pending)]
    [InlineData(MediaStatus.Quarantined)]
    [InlineData(MediaStatus.Active)]
    [InlineData(MediaStatus.Rejected)]
    public async Task Handle_FileInVariousStatuses_CanBeDeleted(MediaStatus initialStatus)
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);

        // Set the status through state transitions
        switch (initialStatus)
        {
            case MediaStatus.Quarantined:
                file.MarkAsQuarantined();
                break;
            case MediaStatus.Active:
                file.MarkAsActive();
                break;
            case MediaStatus.Rejected:
                file.MarkAsQuarantined();
                file.MarkAsRejected("Test rejection");
                break;
        }

        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var command = new DeleteMediaCommand(file.Id);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var deletedFile = await _context.MediaFiles.FindAsync(file.Id);
        deletedFile!.Status.Should().Be(MediaStatus.Deleted);
        deletedFile.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CancellationRequested_RespectsCancellation()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var command = new DeleteMediaCommand(file.Id);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _handler.Handle(command, cts.Token));
    }

    [Fact]
    public async Task Handle_PublisherThrows_RollsBackTransaction()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        _publisherMock.Setup(x => x.Publish(It.IsAny<MediaDeletedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Publishing failed"));

        var command = new DeleteMediaCommand(file.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.Handle(command, CancellationToken.None));

        // Transaction should have rolled back, file should not be marked as deleted
        var notDeletedFile = await _context.MediaFiles.FindAsync(file.Id);
        notDeletedFile!.IsDeleted.Should().BeFalse();
        notDeletedFile.Status.Should().NotBe(MediaStatus.Deleted);
    }
}