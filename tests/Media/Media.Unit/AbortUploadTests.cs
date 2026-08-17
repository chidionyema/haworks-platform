using FluentAssertions;
using Haworks.BuildingBlocks.CurrentUser;
using Haworks.Media.Api.Application;
using Haworks.Media.Api.Domain;
using Haworks.Media.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Haworks.Media.Unit;

public class AbortUploadTests
{
    private const string OwnerId = "abort-owner-1";
    private readonly MediaDbContext _context;
    private readonly Mock<IS3Service> _s3Mock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly AbortUploadHandler _handler;

    public AbortUploadTests()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new MediaDbContext(options);
        _s3Mock = new Mock<IS3Service>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(x => x.UserId).Returns(OwnerId);
        _handler = new AbortUploadHandler(_context, _s3Mock.Object, _currentUserMock.Object, TimeProvider.System);
    }

    [Fact]
    public async Task Handle_PendingSinglePartUpload_ShouldMarkDeletedWithoutS3Call()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var result = await _handler.Handle(new AbortUploadCommand(file.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var updated = await _context.MediaFiles.FindAsync(file.Id);
        updated!.IsDeleted.Should().BeTrue();
        _s3Mock.Verify(x => x.AbortMultipartUploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PendingMultipartUpload_ShouldAbortInS3AndMarkDeleted()
    {
        var file = MediaFile.Create("large.mp4", new string('a', 64), 50_000_000, "video/mp4", OwnerId);
        file.InitiateMultipart("s3-upload-abort-1", 3);
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var result = await _handler.Handle(new AbortUploadCommand(file.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var updated = await _context.MediaFiles.FindAsync(file.Id);
        updated!.IsDeleted.Should().BeTrue();
        _s3Mock.Verify(x => x.AbortMultipartUploadAsync(file.Id.ToString(), "s3-upload-abort-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NonPendingFile_ShouldReturnInvalidStateFailure()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);
        file.MarkAsQuarantined();
        file.MarkAsActive();
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var result = await _handler.Handle(new AbortUploadCommand(file.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.InvalidState");
        var unchanged = await _context.MediaFiles.FindAsync(file.Id);
        unchanged!.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NotFound_ShouldReturnFailure()
    {
        var result = await _handler.Handle(new AbortUploadCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.NotFound");
    }

    [Fact]
    public async Task Handle_WrongOwner_ShouldReturnForbidden()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "other-owner");
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var result = await _handler.Handle(new AbortUploadCommand(file.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Forbidden");
    }

    [Fact]
    public async Task Handle_NoUserId_ShouldReturnUnauthorized()
    {
        _currentUserMock.Setup(x => x.UserId).Returns(string.Empty);

        var result = await _handler.Handle(new AbortUploadCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Unauthorized");
    }
}
