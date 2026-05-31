using FluentAssertions;
using Haworks.BuildingBlocks.CurrentUser;
using Haworks.Media.Api.Application;
using Haworks.Media.Api.Domain;
using Haworks.Media.Api.Infrastructure;
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

    public BatchInitiateUploadTests()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new MediaDbContext(options);
        _s3ServiceMock = new Mock<IS3Service>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(x => x.UserId).Returns("test-owner-123");

        var uploadOpts = Options.Create(new UploadOptions
        {
            SinglePutMaxBytes = 5_000_000, // 5MB threshold
            PartSizeBytes = 5_242_880 // 5MB parts
        });

        _handler = new BatchInitiateUploadHandler(_context, _s3ServiceMock.Object, _currentUserMock.Object, uploadOpts);
    }

    [Fact]
    public async Task Handle_MixedFileSizes_ShouldReturnMixedUploadTypes()
    {
        // Arrange
        var files = new[]
        {
            new InitiateUploadCommand("small.png", "hash1", 1024, "image/png"),
            new InitiateUploadCommand("medium.jpg", "hash2", 3_000_000, "image/jpeg"),
            new InitiateUploadCommand("large.mp4", "hash3", 50_000_000, "video/mp4")
        };
        var command = new BatchInitiateUploadCommand(files);

        _s3ServiceMock.Setup(x => x.GeneratePreSignedUrl(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("https://test-upload-url.com");

        _s3ServiceMock.Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("multipart-upload-id");

        _s3ServiceMock.Setup(x => x.GeneratePartPresignedUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns((string key, string uploadId, int partNumber) => $"https://part-url/{partNumber}");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);

        // Small file - single upload
        result.Value[0].IsMultipart.Should().BeFalse();
        result.Value[0].UploadUrl.Should().NotBeNull();
        result.Value[0].AlreadyExists.Should().BeFalse();

        // Medium file - single upload (below threshold)
        result.Value[1].IsMultipart.Should().BeFalse();
        result.Value[1].UploadUrl.Should().NotBeNull();
        result.Value[1].AlreadyExists.Should().BeFalse();

        // Large file - multipart upload
        result.Value[2].IsMultipart.Should().BeTrue();
        result.Value[2].UploadUrl.Should().BeNull();
        result.Value[2].S3UploadId.Should().NotBeNull();
        result.Value[2].PartUrls.Should().HaveCount(10); // 50MB / 5MB per part
        result.Value[2].AlreadyExists.Should().BeFalse();

        // Verify DB records were created
        var dbFiles = await _context.MediaFiles.ToListAsync();
        dbFiles.Should().HaveCount(3);
        dbFiles.Where(f => f.UploadKind == UploadKind.SinglePart).Should().HaveCount(2);
        dbFiles.Where(f => f.UploadKind == UploadKind.Multipart).Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_DuplicateFiles_ShouldReturnExistingIds()
    {
        // Arrange
        var existingFile1 = MediaFile.Create("existing1.png", "existing-hash-1", 1024, "image/png", "test-owner-123");
        var existingFile2 = MediaFile.Create("existing2.jpg", "existing-hash-2", 2048, "image/jpeg", "test-owner-123");
        _context.MediaFiles.AddRange(existingFile1, existingFile2);
        await _context.SaveChangesAsync();

        var files = new[]
        {
            new InitiateUploadCommand("duplicate1.png", "existing-hash-1", 1024, "image/png"),
            new InitiateUploadCommand("new.gif", "new-hash", 512, "image/gif"),
            new InitiateUploadCommand("duplicate2.jpg", "existing-hash-2", 2048, "image/jpeg")
        };
        var command = new BatchInitiateUploadCommand(files);

        _s3ServiceMock.Setup(x => x.GeneratePreSignedUrl(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("https://test-upload-url.com");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);

        // First duplicate
        result.Value[0].Id.Should().Be(existingFile1.Id);
        result.Value[0].AlreadyExists.Should().BeTrue();
        result.Value[0].UploadUrl.Should().BeNull();

        // New file
        result.Value[1].AlreadyExists.Should().BeFalse();
        result.Value[1].UploadUrl.Should().NotBeNull();

        // Second duplicate
        result.Value[2].Id.Should().Be(existingFile2.Id);
        result.Value[2].AlreadyExists.Should().BeTrue();
        result.Value[2].UploadUrl.Should().BeNull();

        // Verify only one new DB record was created
        var totalFiles = await _context.MediaFiles.CountAsync();
        totalFiles.Should().Be(3); // 2 existing + 1 new
    }

    [Fact]
    public async Task Handle_NoUserId_ShouldReturnUnauthorized()
    {
        // Arrange
        _currentUserMock.Setup(x => x.UserId).Returns(string.Empty);
        var files = new[] { new InitiateUploadCommand("test.png", "hash123", 1024, "image/png") };
        var command = new BatchInitiateUploadCommand(files);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Unauthorized");
    }

    [Fact]
    public async Task Handle_LargeFileMultipart_ShouldCalculateCorrectPartCount()
    {
        // Arrange
        var files = new[]
        {
            new InitiateUploadCommand("huge.mp4", "hash123", 100_000_000, "video/mp4") // 100MB
        };
        var command = new BatchInitiateUploadCommand(files);

        _s3ServiceMock.Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("multipart-upload-id");

        _s3ServiceMock.Setup(x => x.GeneratePartPresignedUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns((string key, string uploadId, int partNumber) => $"https://part-url/{partNumber}");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);

        var response = result.Value[0];
        response.IsMultipart.Should().BeTrue();
        response.PartCount.Should().Be(20); // 100MB / 5MB per part = 20 parts
        response.PartUrls.Should().HaveCount(20);

        // Verify multipart was initiated
        _s3ServiceMock.Verify(x => x.InitiateMultipartUploadAsync(
            It.IsAny<string>(),
            "video/mp4",
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify the file was saved with correct multipart settings
        var dbFile = await _context.MediaFiles.FirstAsync();
        dbFile.UploadKind.Should().Be(UploadKind.Multipart);
        dbFile.S3UploadId.Should().Be("multipart-upload-id");
    }

    [Fact]
    public async Task Handle_EmptyFilesList_ShouldReturnEmptyResult()
    {
        // Arrange
        var command = new BatchInitiateUploadCommand(Array.Empty<InitiateUploadCommand>());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SingleFileAtThreshold_ShouldUseSinglePut()
    {
        // Arrange - exactly at the 5MB threshold
        var files = new[]
        {
            new InitiateUploadCommand("threshold.mp4", "hash123", 5_000_000, "video/mp4")
        };
        var command = new BatchInitiateUploadCommand(files);

        _s3ServiceMock.Setup(x => x.GeneratePreSignedUrl(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("https://test-upload-url.com");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);

        var response = result.Value[0];
        response.IsMultipart.Should().BeFalse();
        response.UploadUrl.Should().NotBeNull();

        var dbFile = await _context.MediaFiles.FirstAsync();
        dbFile.UploadKind.Should().Be(UploadKind.SinglePart);
    }

    [Fact]
    public async Task Handle_SingleFileOverThreshold_ShouldUseMultipart()
    {
        // Arrange - just over the 5MB threshold
        var files = new[]
        {
            new InitiateUploadCommand("over-threshold.mp4", "hash123", 5_000_001, "video/mp4")
        };
        var command = new BatchInitiateUploadCommand(files);

        _s3ServiceMock.Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("multipart-upload-id");

        _s3ServiceMock.Setup(x => x.GeneratePartPresignedUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns((string key, string uploadId, int partNumber) => $"https://part-url/{partNumber}");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);

        var response = result.Value[0];
        response.IsMultipart.Should().BeTrue();
        response.PartCount.Should().Be(1); // Ceiling of 5_000_001 / 5_242_880
        response.PartUrls.Should().HaveCount(1);

        var dbFile = await _context.MediaFiles.FirstAsync();
        dbFile.UploadKind.Should().Be(UploadKind.Multipart);
    }
}