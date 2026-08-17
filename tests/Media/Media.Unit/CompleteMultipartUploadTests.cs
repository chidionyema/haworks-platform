using FluentAssertions;
using Haworks.BuildingBlocks.CurrentUser;
using Haworks.Media.Api.Application;
using Haworks.Media.Api.Infrastructure;
using Haworks.Media.Api.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Haworks.Media.Unit;

public class CompleteMultipartUploadTests
{
    private const string OwnerId = "multipart-owner-1";
    // SHA-256 of bytes { 0x01, 0x02, 0x03 } used in the mock S3 download
    private const string TestFileHash = "039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81";
    private readonly MediaDbContext _context;
    private readonly Mock<IVirusScanner> _scannerMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly Mock<IS3Service> _s3Mock;
    private readonly CompleteMultipartUploadHandler _handler;

    public CompleteMultipartUploadTests()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _context = new MediaDbContext(options);
        _scannerMock = new Mock<IVirusScanner>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(x => x.UserId).Returns(OwnerId);
        _s3Mock = new Mock<IS3Service>();
        _s3Mock.Setup(x => x.DownloadToFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string destPath, CancellationToken _) =>
            {
                File.WriteAllBytes(destPath, new byte[] { 1, 2, 3 });
                return Task.FromResult(destPath);
            });
        var publisherMock = new Mock<IPublishEndpoint>();
        var sendEndpointMock = new Mock<ISendEndpointProvider>();
        sendEndpointMock.Setup(x => x.GetSendEndpoint(It.IsAny<Uri>()))
            .ReturnsAsync(Mock.Of<ISendEndpoint>());

        _handler = new CompleteMultipartUploadHandler(
            _context, _s3Mock.Object, _scannerMock.Object, _currentUserMock.Object,
            publisherMock.Object, sendEndpointMock.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CompleteMultipartUploadHandler>.Instance);
    }

    private static MediaFile CreateMultipartFile(string hash)
    {
        var file = MediaFile.Create("large.mp4", hash, 50_000_000, "video/mp4", OwnerId);
        file.InitiateMultipart("s3-upload-1", 2);
        return file;
    }

    private static CompleteMultipartUploadCommand CommandFor(Guid mediaId) =>
        new(mediaId, [new PartETagDto(1, "etag-1"), new PartETagDto(2, "etag-2")]);

    [Fact]
    public async Task Handle_CleanFile_ShouldActivateAndCompleteMultipartInS3()
    {
        var mediaFile = CreateMultipartFile(TestFileHash);
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();
        _scannerMock.Setup(x => x.ScanFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _handler.Handle(CommandFor(mediaFile.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var updated = await _context.MediaFiles.FindAsync(mediaFile.Id);
        updated!.Status.Should().Be(MediaStatus.Active);
        _s3Mock.Verify(x => x.CompleteMultipartUploadAsync(mediaFile.Id.ToString(), "s3-upload-1",
            It.IsAny<System.Collections.Generic.IList<Amazon.S3.Model.PartETag>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_InfectedFile_ShouldReject()
    {
        var mediaFile = CreateMultipartFile(TestFileHash);
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();
        _scannerMock.Setup(x => x.ScanFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _handler.Handle(CommandFor(mediaFile.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var updated = await _context.MediaFiles.FindAsync(mediaFile.Id);
        updated!.Status.Should().Be(MediaStatus.Rejected);
    }

    [Fact]
    public async Task Handle_HashMismatch_ShouldRejectWithoutScanning()
    {
        var mediaFile = CreateMultipartFile(new string('e', 64));
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var result = await _handler.Handle(CommandFor(mediaFile.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.HashMismatch");
        var updated = await _context.MediaFiles.FindAsync(mediaFile.Id);
        updated!.Status.Should().Be(MediaStatus.Rejected);
        _scannerMock.Verify(x => x.ScanFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NotInitiatedAsMultipart_ShouldReturnNotMultipartFailure()
    {
        var singlePartFile = MediaFile.Create("single.png", TestFileHash, 1024, "image/png", OwnerId);
        _context.MediaFiles.Add(singlePartFile);
        await _context.SaveChangesAsync();

        var result = await _handler.Handle(CommandFor(singlePartFile.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.NotMultipart");
    }

    [Fact]
    public async Task Handle_WrongState_ShouldReturnInvalidStateFailure()
    {
        var mediaFile = CreateMultipartFile(TestFileHash);
        mediaFile.MarkAsQuarantined();
        mediaFile.MarkAsRejected();
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var result = await _handler.Handle(CommandFor(mediaFile.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.InvalidState");
    }

    [Fact]
    public async Task Handle_NotFound_ShouldReturnFailure()
    {
        var result = await _handler.Handle(CommandFor(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.NotFound");
    }

    [Fact]
    public async Task Handle_WrongOwner_ShouldReturnForbidden()
    {
        var mediaFile = MediaFile.Create("large.mp4", TestFileHash, 50_000_000, "video/mp4", "other-owner");
        mediaFile.InitiateMultipart("s3-upload-1", 2);
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var result = await _handler.Handle(CommandFor(mediaFile.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Forbidden");
    }

    [Fact]
    public async Task Handle_NoUserId_ShouldReturnUnauthorized()
    {
        _currentUserMock.Setup(x => x.UserId).Returns(string.Empty);

        var result = await _handler.Handle(CommandFor(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Unauthorized");
    }
}
