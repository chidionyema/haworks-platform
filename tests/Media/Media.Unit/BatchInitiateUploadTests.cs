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

public class BatchInitiateUploadTests
{
    private readonly MediaDbContext _context;
    private readonly Mock<IS3Service> _s3ServiceMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly BatchInitiateUploadHandler _handler;
    private const string OwnerId = "test-owner-123";
    private readonly UploadOptions _uploadOpts = new() { SinglePutMaxBytes = 5_000_000, PartSizeBytes = 10_000_000 };

    public BatchInitiateUploadTests()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new MediaDbContext(options);
        _s3ServiceMock = new Mock<IS3Service>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(x => x.UserId).Returns(OwnerId);
        _handler = new BatchInitiateUploadHandler(_context, _s3ServiceMock.Object, _currentUserMock.Object, Options.Create(_uploadOpts));
    }

    [Fact]
    public async Task Handle_EmptyUserId_ReturnsUnauthorized()
    {
        _currentUserMock.Setup(x => x.UserId).Returns((string)null!);
        var command = new BatchInitiateUploadCommand([
            new InitiateUploadCommand("test.png", new string('a', 64), 1024, "image/png")
        ]);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Media.Unauthorized");
    }

    [Fact]
    public async Task Handle_SingleNewFile_SmallSize_CreatesMediaAndReturnsPresignedUrl()
    {
        var command = new BatchInitiateUploadCommand([
            new InitiateUploadCommand("test.png", new string('a', 64), 1024, "image/png")
        ]);
        _s3ServiceMock.Setup(x => x.GeneratePreSignedUrl(It.IsAny<string>(), "image/png"))
            .Returns("http://upload-url");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        var response = result.Value.First();
        response.UploadUrl.Should().Be("http://upload-url");
        response.AlreadyExists.Should().BeFalse();
        response.IsMultipart.Should().BeFalse();

        var file = await _context.MediaFiles.FindAsync(response.Id);
        file.Should().NotBeNull();
        file!.Status.Should().Be(MediaStatus.Pending);
        file.UploadKind.Should().Be(UploadKind.SinglePart);
    }

    [Fact]
    public async Task Handle_SingleNewFile_LargeSize_CreatesMultipartUpload()
    {
        var largeFileSize = _uploadOpts.SinglePutMaxBytes + 1;
        var command = new BatchInitiateUploadCommand([
            new InitiateUploadCommand("large.mp4", new string('b', 64), largeFileSize, "video/mp4")
        ]);
        _s3ServiceMock.Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<string>(), "video/mp4", It.IsAny<CancellationToken>()))
            .ReturnsAsync("upload-id-123");
        _s3ServiceMock.Setup(x => x.GeneratePartPresignedUrl(It.IsAny<string>(), "upload-id-123", It.IsAny<int>()))
            .Returns((string key, string uploadId, int partNumber) => $"http://part-{partNumber}-url");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var response = result.Value.First();
        response.IsMultipart.Should().BeTrue();
        response.S3UploadId.Should().Be("upload-id-123");
        response.PartCount.Should().Be(1); // Math.Ceiling((double)(5_000_001) / 10_000_000) = 1
        response.PartUrls.Should().NotBeEmpty();
        response.PartUrls.First().Should().Be("http://part-1-url");

        var file = await _context.MediaFiles.FindAsync(response.Id);
        file.Should().NotBeNull();
        file!.UploadKind.Should().Be(UploadKind.Multipart);
        file.S3UploadId.Should().Be("upload-id-123");
        file.PartCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_MultipleFiles_ProcessesAll()
    {
        var command = new BatchInitiateUploadCommand([
            new InitiateUploadCommand("file1.png", new string('a', 64), 1024, "image/png"),
            new InitiateUploadCommand("file2.jpg", new string('b', 64), 2048, "image/jpeg"),
            new InitiateUploadCommand("file3.pdf", new string('c', 64), 4096, "application/pdf")
        ]);
        _s3ServiceMock.Setup(x => x.GeneratePreSignedUrl(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("http://upload-url");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value.Should().OnlyContain(r => !r.AlreadyExists);
        result.Value.Should().OnlyContain(r => !r.IsMultipart);

        var files = await _context.MediaFiles.ToListAsync();
        files.Should().HaveCount(3);
        files.Should().OnlyContain(f => f.OwnerId == OwnerId);
        files.Should().OnlyContain(f => f.Status == MediaStatus.Pending);
    }

    [Fact]
    public async Task Handle_ExistingFileByHash_ReturnsExistingId()
    {
        var hash = new string('x', 64);
        var existingFile = MediaFile.Create("existing.png", hash, 1024, "image/png", OwnerId);
        _context.MediaFiles.Add(existingFile);
        await _context.SaveChangesAsync();

        var command = new BatchInitiateUploadCommand([
            new InitiateUploadCommand("duplicate.png", hash, 1024, "image/png")
        ]);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var response = result.Value.First();
        response.Id.Should().Be(existingFile.Id);
        response.AlreadyExists.Should().BeTrue();
        response.UploadUrl.Should().BeNull();

        var filesCount = await _context.MediaFiles.CountAsync();
        filesCount.Should().Be(1); // No new file created
    }

    [Fact]
    public async Task Handle_MixedNewAndExistingFiles_ReturnsAppropriateResponses()
    {
        var existingHash = new string('x', 64);
        var existingFile = MediaFile.Create("existing.png", existingHash, 1024, "image/png", OwnerId);
        _context.MediaFiles.Add(existingFile);
        await _context.SaveChangesAsync();

        var command = new BatchInitiateUploadCommand([
            new InitiateUploadCommand("new.png", new string('a', 64), 1024, "image/png"),
            new InitiateUploadCommand("duplicate.png", existingHash, 1024, "image/png"),
            new InitiateUploadCommand("another-new.jpg", new string('b', 64), 2048, "image/jpeg")
        ]);
        _s3ServiceMock.Setup(x => x.GeneratePreSignedUrl(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("http://upload-url");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);

        var newFiles = result.Value.Where(r => !r.AlreadyExists).ToList();
        var existingFiles = result.Value.Where(r => r.AlreadyExists).ToList();

        newFiles.Should().HaveCount(2);
        newFiles.Should().OnlyContain(r => r.UploadUrl == "http://upload-url");

        existingFiles.Should().HaveCount(1);
        existingFiles.First().Id.Should().Be(existingFile.Id);
        existingFiles.First().UploadUrl.Should().BeNull();

        var totalFilesCount = await _context.MediaFiles.CountAsync();
        totalFilesCount.Should().Be(3); // 1 existing + 2 new
    }

    [Fact]
    public async Task Handle_DifferentOwnerExistingFile_CreatesNewFile()
    {
        var hash = new string('x', 64);
        var otherOwnerFile = MediaFile.Create("other-owner.png", hash, 1024, "image/png", "other-owner-id");
        _context.MediaFiles.Add(otherOwnerFile);
        await _context.SaveChangesAsync();

        var command = new BatchInitiateUploadCommand([
            new InitiateUploadCommand("same-hash.png", hash, 1024, "image/png")
        ]);
        _s3ServiceMock.Setup(x => x.GeneratePreSignedUrl(It.IsAny<string>(), "image/png"))
            .Returns("http://upload-url");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var response = result.Value.First();
        response.Id.Should().NotBe(otherOwnerFile.Id);
        response.AlreadyExists.Should().BeFalse();
        response.UploadUrl.Should().Be("http://upload-url");

        var filesCount = await _context.MediaFiles.CountAsync();
        filesCount.Should().Be(2); // Both files exist, different owners
    }

    [Theory]
    [InlineData(10_000_000, 1)] // Exactly one part
    [InlineData(10_000_001, 2)] // Just over one part
    [InlineData(25_000_000, 3)] // Multiple parts
    public async Task Handle_LargeFile_CalculatesCorrectPartCount(long fileSize, int expectedParts)
    {
        var command = new BatchInitiateUploadCommand([
            new InitiateUploadCommand("large.mp4", new string('b', 64), fileSize, "video/mp4")
        ]);
        _s3ServiceMock.Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<string>(), "video/mp4", It.IsAny<CancellationToken>()))
            .ReturnsAsync("upload-id-123");
        _s3ServiceMock.Setup(x => x.GeneratePartPresignedUrl(It.IsAny<string>(), "upload-id-123", It.IsAny<int>()))
            .Returns((string key, string uploadId, int partNumber) => $"http://part-{partNumber}-url");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var response = result.Value.First();
        response.PartCount.Should().Be(expectedParts);
        response.PartUrls.Should().HaveCount(expectedParts);

        for (int i = 1; i <= expectedParts; i++)
        {
            response.PartUrls.Should().Contain($"http://part-{i}-url");
        }
    }

    [Fact]
    public async Task Handle_MultipartUpload_SetsCorrectDatabaseFields()
    {
        var largeFileSize = _uploadOpts.SinglePutMaxBytes + 1;
        var command = new BatchInitiateUploadCommand([
            new InitiateUploadCommand("large.mp4", new string('b', 64), largeFileSize, "video/mp4")
        ]);
        _s3ServiceMock.Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<string>(), "video/mp4", It.IsAny<CancellationToken>()))
            .ReturnsAsync("upload-id-456");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var file = await _context.MediaFiles.FindAsync(result.Value.First().Id);
        file!.UploadKind.Should().Be(UploadKind.Multipart);
        file.S3UploadId.Should().Be("upload-id-456");
        file.PartCount.Should().BeGreaterThan(0);
    }
}