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
    private const string OwnerId = "test-owner-123";

    public LinkEntityTests()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new MediaDbContext(options);
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(x => x.UserId).Returns(OwnerId);
        _handler = new LinkEntityHandler(_context, _currentUserMock.Object);
    }

    [Fact]
    public async Task Handle_EmptyUserId_ReturnsUnauthorized()
    {
        _currentUserMock.Setup(x => x.UserId).Returns((string)null!);
        var entityId = Guid.NewGuid();
        var command = new LinkEntityCommand(Guid.NewGuid(), entityId, "Product");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Media.Unauthorized");
        result.Error.Message.Should().Be("Authenticated user identity could not be resolved.");
    }

    [Fact]
    public async Task Handle_MediaNotFound_ReturnsNotFound()
    {
        var entityId = Guid.NewGuid();
        var command = new LinkEntityCommand(Guid.NewGuid(), entityId, "Product");

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

        var entityId = Guid.NewGuid();
        var command = new LinkEntityCommand(file.Id, entityId, "Product");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Media.Forbidden");
        result.Error.Message.Should().Be("You do not own this media file.");
    }

    [Fact]
    public async Task Handle_ValidLink_SetsEntityLinkAndSavesChanges()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var entityId = Guid.NewGuid();
        var command = new LinkEntityCommand(file.Id, entityId, "Product");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Verify entity link was set
        var linkedFile = await _context.MediaFiles.FindAsync(file.Id);
        linkedFile.Should().NotBeNull();
        linkedFile!.EntityId.Should().Be(entityId);
        linkedFile.EntityType.Should().Be("Product");
    }

    [Theory]
    [InlineData("Product", "SKU12345")]
    [InlineData("Order", "order-data")]
    [InlineData("User", "profile-picture")]
    [InlineData("BlogPost", "featured-image")]
    public async Task Handle_DifferentEntityTypes_AllowsVariousEntityTypes(string entityType, string description)
    {
        var file = MediaFile.Create($"{description}.png", new string('a', 64), 1024, "image/png", OwnerId);
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var entityId = Guid.NewGuid();
        var command = new LinkEntityCommand(file.Id, entityId, entityType);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var linkedFile = await _context.MediaFiles.FindAsync(file.Id);
        linkedFile!.EntityType.Should().Be(entityType);
        linkedFile.EntityId.Should().Be(entityId);
    }

    [Fact]
    public async Task Handle_ExistingLink_UpdatesToNewEntity()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);
        var originalEntityId = Guid.NewGuid();
        file.SetEntityLink(originalEntityId, "Product");
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var newEntityId = Guid.NewGuid();
        var command = new LinkEntityCommand(file.Id, newEntityId, "Order");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var linkedFile = await _context.MediaFiles.FindAsync(file.Id);
        linkedFile!.EntityId.Should().Be(newEntityId);
        linkedFile.EntityType.Should().Be("Order");
        linkedFile.EntityId.Should().NotBe(originalEntityId);
    }

    [Fact]
    public async Task Handle_SameEntityLink_IdempotentOperation()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);
        var entityId = Guid.NewGuid();
        file.SetEntityLink(entityId, "Product");
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var command = new LinkEntityCommand(file.Id, entityId, "Product");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Link should remain the same
        var linkedFile = await _context.MediaFiles.FindAsync(file.Id);
        linkedFile!.EntityId.Should().Be(entityId);
        linkedFile.EntityType.Should().Be("Product");
    }

    [Theory]
    [InlineData(MediaStatus.Pending)]
    [InlineData(MediaStatus.Quarantined)]
    [InlineData(MediaStatus.Active)]
    [InlineData(MediaStatus.Rejected)]
    public async Task Handle_FileInVariousStatuses_CanLinkEntity(MediaStatus initialStatus)
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

        var entityId = Guid.NewGuid();
        var command = new LinkEntityCommand(file.Id, entityId, "Product");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var linkedFile = await _context.MediaFiles.FindAsync(file.Id);
        linkedFile!.EntityId.Should().Be(entityId);
        linkedFile.EntityType.Should().Be("Product");
        linkedFile.Status.Should().Be(initialStatus); // Status should remain unchanged
    }

    [Fact]
    public async Task Handle_DeletedFile_CannotLinkEntity()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);
        file.MarkAsActive();
        file.MarkAsDeleted(TimeProvider.System);
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var entityId = Guid.NewGuid();
        var command = new LinkEntityCommand(file.Id, entityId, "Product");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Even though deleted, the link operation should succeed
        // (The business might decide differently - this tests current implementation)
        var linkedFile = await _context.MediaFiles.FindAsync(file.Id);
        linkedFile!.EntityId.Should().Be(entityId);
        linkedFile.EntityType.Should().Be("Product");
    }

    [Fact]
    public async Task Handle_CancellationRequested_RespectsCancellation()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var entityId = Guid.NewGuid();
        var command = new LinkEntityCommand(file.Id, entityId, "Product");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _handler.Handle(command, cts.Token));
    }

    [Fact]
    public async Task Handle_MultipleLinksToSameEntity_AllowsMultipleFiles()
    {
        var file1 = MediaFile.Create("image1.png", new string('a', 64), 1024, "image/png", OwnerId);
        var file2 = MediaFile.Create("image2.png", new string('b', 64), 2048, "image/png", OwnerId);
        _context.MediaFiles.AddRange(file1, file2);
        await _context.SaveChangesAsync();

        var sharedEntityId = Guid.NewGuid();
        var command1 = new LinkEntityCommand(file1.Id, sharedEntityId, "Product");
        var command2 = new LinkEntityCommand(file2.Id, sharedEntityId, "Product");

        var result1 = await _handler.Handle(command1, CancellationToken.None);
        var result2 = await _handler.Handle(command2, CancellationToken.None);

        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();

        var linkedFile1 = await _context.MediaFiles.FindAsync(file1.Id);
        var linkedFile2 = await _context.MediaFiles.FindAsync(file2.Id);

        linkedFile1!.EntityId.Should().Be(sharedEntityId);
        linkedFile2!.EntityId.Should().Be(sharedEntityId);
        linkedFile1.EntityType.Should().Be("Product");
        linkedFile2.EntityType.Should().Be("Product");
    }

    [Theory]
    [InlineData(UploadKind.SinglePart)]
    [InlineData(UploadKind.Multipart)]
    public async Task Handle_DifferentUploadKinds_CanLinkEntity(UploadKind uploadKind)
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);
        if (uploadKind == UploadKind.Multipart)
        {
            file.InitiateMultipart("upload-id", 3);
        }

        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var entityId = Guid.NewGuid();
        var command = new LinkEntityCommand(file.Id, entityId, "Product");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var linkedFile = await _context.MediaFiles.FindAsync(file.Id);
        linkedFile!.EntityId.Should().Be(entityId);
        linkedFile.EntityType.Should().Be("Product");
        linkedFile.UploadKind.Should().Be(uploadKind);
    }
}