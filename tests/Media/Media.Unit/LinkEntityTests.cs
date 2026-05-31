using FluentAssertions;
using Haworks.BuildingBlocks.CurrentUser;
using Haworks.Media.Api.Application;
using Haworks.Media.Api.Domain;
using Haworks.Media.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Haworks.Media.Unit;

public class LinkEntityTests
{
    private readonly MediaDbContext _context;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly LinkEntityHandler _handler;

    public LinkEntityTests()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new MediaDbContext(options);
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(x => x.UserId).Returns("test-owner-123");

        _handler = new LinkEntityHandler(_context, _currentUserMock.Object);
    }

    [Fact]
    public async Task Handle_ValidRequest_ShouldLinkEntity()
    {
        // Arrange
        var mediaFile = MediaFile.Create("test.png", "hash123", 1024, "image/png", "test-owner-123");
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var entityId = Guid.NewGuid();
        var command = new LinkEntityCommand(mediaFile.Id, entityId, "Product");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var updated = await _context.MediaFiles.FindAsync(mediaFile.Id);
        updated!.EntityId.Should().Be(entityId);
        updated.EntityType.Should().Be("Product");
    }

    [Fact]
    public async Task Handle_NoUserId_ShouldReturnUnauthorized()
    {
        // Arrange
        _currentUserMock.Setup(x => x.UserId).Returns(string.Empty);
        var command = new LinkEntityCommand(Guid.NewGuid(), Guid.NewGuid(), "Product");

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
        var command = new LinkEntityCommand(Guid.NewGuid(), Guid.NewGuid(), "Product");

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

        var command = new LinkEntityCommand(mediaFile.Id, Guid.NewGuid(), "Product");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Forbidden");
    }

    [Fact]
    public async Task Handle_UpdateExistingLink_ShouldOverwritePreviousLink()
    {
        // Arrange
        var mediaFile = MediaFile.Create("test.png", "hash123", 1024, "image/png", "test-owner-123");
        var originalEntityId = Guid.NewGuid();
        mediaFile.SetEntityLink(originalEntityId, "Order");
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var newEntityId = Guid.NewGuid();
        var command = new LinkEntityCommand(mediaFile.Id, newEntityId, "Product");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var updated = await _context.MediaFiles.FindAsync(mediaFile.Id);
        updated!.EntityId.Should().Be(newEntityId);
        updated.EntityType.Should().Be("Product");
        updated.EntityId.Should().NotBe(originalEntityId);
    }

    [Theory]
    [InlineData("Product")]
    [InlineData("Order")]
    [InlineData("Customer")]
    [InlineData("Invoice")]
    [InlineData("DocumentTemplate")]
    public async Task Handle_ValidEntityTypes_ShouldAcceptDifferentEntityTypes(string entityType)
    {
        // Arrange
        var mediaFile = MediaFile.Create("test.png", "hash123", 1024, "image/png", "test-owner-123");
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var entityId = Guid.NewGuid();
        var command = new LinkEntityCommand(mediaFile.Id, entityId, entityType);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var updated = await _context.MediaFiles.FindAsync(mediaFile.Id);
        updated!.EntityId.Should().Be(entityId);
        updated.EntityType.Should().Be(entityType);
    }

    [Fact]
    public async Task Handle_SameEntityLinkTwice_ShouldBeIdempotent()
    {
        // Arrange
        var mediaFile = MediaFile.Create("test.png", "hash123", 1024, "image/png", "test-owner-123");
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var entityId = Guid.NewGuid();
        var command = new LinkEntityCommand(mediaFile.Id, entityId, "Product");

        // Act - First call
        var result1 = await _handler.Handle(command, CancellationToken.None);

        // Act - Second call with same parameters
        var result2 = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();

        var updated = await _context.MediaFiles.FindAsync(mediaFile.Id);
        updated!.EntityId.Should().Be(entityId);
        updated.EntityType.Should().Be("Product");
    }

    [Theory]
    [InlineData(MediaStatus.Pending)]
    [InlineData(MediaStatus.Quarantined)]
    [InlineData(MediaStatus.Active)]
    [InlineData(MediaStatus.Rejected)]
    public async Task Handle_FileInAnyStatus_ShouldAllowLinking(MediaStatus status)
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

        var entityId = Guid.NewGuid();
        var command = new LinkEntityCommand(mediaFile.Id, entityId, "Product");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var updated = await _context.MediaFiles.FindAsync(mediaFile.Id);
        updated!.EntityId.Should().Be(entityId);
        updated.EntityType.Should().Be("Product");
    }

    [Fact]
    public async Task Handle_InvalidEntityType_ShouldReturnValidationError()
    {
        // Arrange
        var mediaFile = MediaFile.Create("test.png", "hash123", 1024, "image/png", "test-owner-123");
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        // Empty entity type should fail validation
        var command = new LinkEntityCommand(mediaFile.Id, Guid.NewGuid(), string.Empty);

        // Act & Assert
        // This should fail at the validation level or throw an exception
        await Assert.ThrowsAsync<ArgumentException>(() => _handler.Handle(command, CancellationToken.None));
    }
}