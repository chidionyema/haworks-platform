using FluentAssertions;
using Haworks.BuildingBlocks.CurrentUser;
using Haworks.Media.Api.Application;
using Haworks.Media.Api.Infrastructure;
using Haworks.Media.Api.Domain;
using Haworks.Media.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Haworks.Media.Unit;

public class InitiateUploadTests
{
    private readonly MediaDbContext _context;
    private readonly Mock<IS3Service> _s3ServiceMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly InitiateUploadHandler _handler;

    public InitiateUploadTests()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new MediaDbContext(options);
        _s3ServiceMock = new Mock<IS3Service>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(x => x.UserId).Returns("test-owner-123");
        var uploadOpts = Options.Create(new UploadOptions());
        _handler = new InitiateUploadHandler(_context, _s3ServiceMock.Object, _currentUserMock.Object, uploadOpts);
    }

    [Fact]
    public async Task Handle_NewFile_ShouldCreateMediaAndReturnUrl()
    {
        var command = new InitiateUploadCommand("test.png", new string('a', 64), 1024, "image/png");
        _s3ServiceMock.Setup(x => x.GeneratePreSignedUrl(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("http://upload-url");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UploadUrl.Should().Be("http://upload-url");
        result.Value.AlreadyExists.Should().BeFalse();

        var file = await _context.MediaFiles.FindAsync(result.Value.Id);
        file.Should().NotBeNull();
        file!.Status.Should().Be(MediaStatus.Pending);
    }

    [Fact]
    public async Task Handle_DuplicateHash_ShouldReturnExistingMediaId()
    {
        var hash = new string('b', 64);
        var existingFile = MediaFile.Create("old.png", hash, 512, "image/png", "test-owner-123");
        _context.MediaFiles.Add(existingFile);
        await _context.SaveChangesAsync();

        var command = new InitiateUploadCommand("new.png", hash, 1024, "image/png");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(existingFile.Id);
        result.Value.AlreadyExists.Should().BeTrue();
        result.Value.UploadUrl.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoUserId_ShouldReturnUnauthorized()
    {
        _currentUserMock.Setup(x => x.UserId).Returns(string.Empty);

        var command = new InitiateUploadCommand("test.png", new string('a', 64), 1024, "image/png");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Unauthorized");
    }

    [Fact]
    public async Task Handle_FileLargerThanSinglePutMax_ShouldInitiateMultipartUpload()
    {
        // 50MB > default SinglePutMaxBytes (8MB); PartSizeBytes default 26,214,400 -> 2 parts
        var command = new InitiateUploadCommand("large.mp4", new string('d', 64), 50_000_000, "video/mp4");
        _s3ServiceMock.Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("s3-upload-id-1");
        _s3ServiceMock.Setup(x => x.GeneratePartPresignedUrl(It.IsAny<string>(), "s3-upload-id-1", It.IsAny<int>()))
            .Returns<string, string, int>((key, uploadId, partNumber) => $"http://upload-url/part-{partNumber}");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsMultipart.Should().BeTrue();
        result.Value.S3UploadId.Should().Be("s3-upload-id-1");
        result.Value.PartCount.Should().Be(2);
        result.Value.PartUrls.Should().HaveCount(2);
        result.Value.UploadUrl.Should().BeNull();

        var file = await _context.MediaFiles.FindAsync(result.Value.Id);
        file!.UploadKind.Should().Be(UploadKind.Multipart);
        file.S3UploadId.Should().Be("s3-upload-id-1");
    }

    [Fact]
    public async Task Handle_PartCountExceedsTenThousand_ShouldReturnTooManyPartsFailure()
    {
        // 270GB / 26,214,400 bytes-per-part -> ~10,300 parts, still under 256GB MaxFileSizeBytes
        var command = new InitiateUploadCommand("huge.mp4", new string('f', 64), 270_000_000_000, "video/mp4");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.TooManyParts");
        _s3ServiceMock.Verify(x => x.InitiateMultipartUploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
