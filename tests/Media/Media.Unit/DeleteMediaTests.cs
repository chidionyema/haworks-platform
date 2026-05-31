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

    public DeleteMediaTests()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new MediaDbContext(options);
        _currentUserMock = new Mock<ICurrentUserService>();
        _publisherMock = new Mock<IPublishEndpoint>();
        _timeProviderMock = new Mock<TimeProvider>();

        _currentUserMock.Setup(x => x.UserId).Returns("test-owner-123");
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(2026, 5, 31, 14, 30, 0, TimeSpan.Zero));

        _handler = new DeleteMediaHandler(_context, _currentUserMock.Object, _publisherMock.Object, _timeProviderMock.Object);
    }

    [Fact]
    public async Task Handle_ValidFile_ShouldMarkDeleted()
    {
        // Arrange
        var mediaFile = MediaFile.Create("test.png", "hash123", 1024, "image/png", "test-owner-123");
        mediaFile.SetEntityLink(Guid.NewGuid(), "Product");
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var command = new DeleteMediaCommand(mediaFile.Id);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify file is marked as deleted (soft delete)
        var updated = await _context.MediaFiles.IgnoreQueryFilters().FirstAsync(f => f.Id == mediaFile.Id);
        updated.IsDeleted.Should().BeTrue();
        updated.DeletedAt.Should().NotBeNull();

        // Verify event was published
        _publisherMock.Verify(x => x.Publish(
            It.Is<MediaDeletedEvent>(e =>
                e.MediaId == mediaFile.Id &&
                e.OwnerId == "test-owner-123" &&
                e.EntityId == mediaFile.EntityId &&
                e.EntityType == "Product"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoUserId_ShouldReturnUnauthorized()
    {
        // Arrange
        _currentUserMock.Setup(x => x.UserId).Returns(string.Empty);
        var command = new DeleteMediaCommand(Guid.NewGuid());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Unauthorized");
    }

    [Fact]
    public async Task Handle_MediaFileNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var command = new DeleteMediaCommand(Guid.NewGuid());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.NotFound");
    }

    [Fact]
    public async Task Handle_NotOwner_ShouldReturnForbidden()
    {
        // Arrange
        var mediaFile = MediaFile.Create("test.png", "hash123", 1024, "image/png", "other-owner");
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var command = new DeleteMediaCommand(mediaFile.Id);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Forbidden");
    }

    [Fact]
    public async Task Handle_FileWithoutEntityLink_ShouldPublishEventWithNullEntity()
    {
        // Arrange
        var mediaFile = MediaFile.Create("test.png", "hash123", 1024, "image/png", "test-owner-123");
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var command = new DeleteMediaCommand(mediaFile.Id);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify event was published with null entity fields
        _publisherMock.Verify(x => x.Publish(
            It.Is<MediaDeletedEvent>(e =>
                e.MediaId == mediaFile.Id &&
                e.OwnerId == "test-owner-123" &&
                e.EntityId == null &&
                e.EntityType == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyDeletedFile_ShouldReturnNotFound()
    {
        // Arrange
        var mediaFile = MediaFile.Create("test.png", "hash123", 1024, "image/png", "test-owner-123");
        mediaFile.MarkAsDeleted(_timeProviderMock.Object);
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var command = new DeleteMediaCommand(mediaFile.Id);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.NotFound");

        // Verify no event was published since file was not found (due to soft delete filter)
        _publisherMock.Verify(x => x.Publish(It.IsAny<MediaDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TransactionCommitted_ShouldEnsureAtomicity()
    {
        // Arrange
        var mediaFile = MediaFile.Create("test.png", "hash123", 1024, "image/png", "test-owner-123");
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var command = new DeleteMediaCommand(mediaFile.Id);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify that both database update and event publish happened together
        var updated = await _context.MediaFiles.IgnoreQueryFilters().FirstAsync(f => f.Id == mediaFile.Id);
        updated.IsDeleted.Should().BeTrue();

        _publisherMock.Verify(x => x.Publish(
            It.IsAny<MediaDeletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(MediaStatus.Pending)]
    [InlineData(MediaStatus.Quarantined)]
    [InlineData(MediaStatus.Active)]
    [InlineData(MediaStatus.Rejected)]
    public async Task Handle_FileInAnyStatus_ShouldAllowDeletion(MediaStatus status)
    {
        // Arrange
        var mediaFile = MediaFile.Create("test.png", "hash123", 1024, "image/png", "test-owner-123");

        // Set the file to the specific status
        switch (status)
        {
            case MediaStatus.Quarantined:
                mediaFile.MarkAsQuarantined();
                break;
            case MediaStatus.Active:
                mediaFile.MarkAsQuarantined();
                mediaFile.MarkAsActive();
                break;
            case MediaStatus.Rejected:
                mediaFile.MarkAsQuarantined();
                mediaFile.MarkAsRejected();
                break;
            // Pending is the default
        }

        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var command = new DeleteMediaCommand(mediaFile.Id);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var updated = await _context.MediaFiles.IgnoreQueryFilters().FirstAsync(f => f.Id == mediaFile.Id);
        updated.IsDeleted.Should().BeTrue();
    }
}