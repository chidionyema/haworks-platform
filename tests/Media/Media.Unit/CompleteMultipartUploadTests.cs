using FluentAssertions;
using Haworks.BuildingBlocks.CurrentUser;
using Haworks.Contracts.Media;
using Haworks.Media.Api.Application;
using Haworks.Media.Api.Domain;
using Haworks.Media.Api.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Haworks.Media.Unit;

public class CompleteMultipartUploadTests
{
    private readonly MediaDbContext _context;
    private readonly Mock<IS3Service> _s3ServiceMock;
    private readonly Mock<IVirusScanner> _virusScannerMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly Mock<IPublishEndpoint> _publisherMock;
    private readonly Mock<ISendEndpoint> _sendEndpointMock;
    private readonly Mock<ISendEndpointProvider> _sendEndpointProviderMock;
    private readonly CompleteMultipartUploadHandler _handler;

    public CompleteMultipartUploadTests()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new MediaDbContext(options);
        _s3ServiceMock = new Mock<IS3Service>();
        _virusScannerMock = new Mock<IVirusScanner>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _publisherMock = new Mock<IPublishEndpoint>();
        _sendEndpointMock = new Mock<ISendEndpoint>();
        _sendEndpointProviderMock = new Mock<ISendEndpointProvider>();

        _currentUserMock.Setup(x => x.UserId).Returns("test-owner-123");
        _sendEndpointProviderMock
            .Setup(x => x.GetSendEndpoint(It.IsAny<Uri>()))
            .ReturnsAsync(_sendEndpointMock.Object);

        _handler = new CompleteMultipartUploadHandler(
            _context,
            _s3ServiceMock.Object,
            _virusScannerMock.Object,
            _currentUserMock.Object,
            _publisherMock.Object,
            _sendEndpointProviderMock.Object,
            Mock.Of<ILogger<CompleteMultipartUploadHandler>>()
        );
    }

    [Fact]
    public async Task Handle_ValidMultipartUpload_ShouldCompleteSuccessfully()
    {
        // Arrange
        var mediaFile = MediaFile.Create("big.mp4", "hash123", 50_000_000, "video/mp4", "test-owner-123");
        mediaFile.InitiateMultipart("upload-id-123", 3);
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var parts = new[]
        {
            new PartETagDto(1, "etag1"),
            new PartETagDto(2, "etag2"),
            new PartETagDto(3, "etag3")
        };
        var command = new CompleteMultipartUploadCommand(mediaFile.Id, parts);

        var tempPath = Path.Combine(Path.GetTempPath(), $"media-scan-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

        // Create a temp file with the expected hash
        var testData = System.Text.Encoding.UTF8.GetBytes("test file content for hash verification");
        await File.WriteAllBytesAsync(tempPath, testData);
        var expectedHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(testData));

        // Update the media file with the correct hash
        mediaFile.GetType().GetProperty("Hash")!.SetValue(mediaFile, expectedHash);
        await _context.SaveChangesAsync();

        _s3ServiceMock.Setup(x => x.DownloadToFileAsync(mediaFile.Id.ToString(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((key, destPath, ct) =>
            {
                File.Copy(tempPath, destPath, true);
            })
            .ReturnsAsync((string key, string destPath, CancellationToken ct) => destPath);

        _virusScannerMock.Setup(x => x.ScanFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var updatedFile = await _context.MediaFiles.FindAsync(mediaFile.Id);
        updatedFile!.Status.Should().Be(MediaStatus.Active);

        // Verify S3 operations were called
        _s3ServiceMock.Verify(x => x.CompleteMultipartUploadAsync(
            mediaFile.Id.ToString(),
            "upload-id-123",
            It.IsAny<IList<Amazon.S3.Model.PartETag>>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify events were published
        _publisherMock.Verify(x => x.Publish(
            It.Is<MediaScanPassedEvent>(e => e.MediaId == mediaFile.Id),
            It.IsAny<CancellationToken>()), Times.Once);

        _sendEndpointMock.Verify(x => x.Send(
            It.Is<ProcessMediaCommand>(c => c.MediaId == mediaFile.Id),
            It.IsAny<CancellationToken>()), Times.Once);

        // Cleanup
        if (File.Exists(tempPath)) File.Delete(tempPath);
    }

    [Fact]
    public async Task Handle_VirusScanFails_ShouldMarkAsRejected()
    {
        // Arrange
        var mediaFile = MediaFile.Create("infected.mp4", "hash123", 50_000_000, "video/mp4", "test-owner-123");
        mediaFile.InitiateMultipart("upload-id-123", 3);
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var parts = new[] { new PartETagDto(1, "etag1") };
        var command = new CompleteMultipartUploadCommand(mediaFile.Id, parts);

        var tempPath = Path.Combine(Path.GetTempPath(), $"media-scan-{Guid.NewGuid()}");
        var testData = System.Text.Encoding.UTF8.GetBytes("test content");
        await File.WriteAllBytesAsync(tempPath, testData);
        var expectedHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(testData));

        mediaFile.GetType().GetProperty("Hash")!.SetValue(mediaFile, expectedHash);
        await _context.SaveChangesAsync();

        _s3ServiceMock.Setup(x => x.DownloadToFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((key, destPath, ct) => File.Copy(tempPath, destPath, true))
            .ReturnsAsync((string key, string destPath, CancellationToken ct) => destPath);

        _virusScannerMock.Setup(x => x.ScanFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var updatedFile = await _context.MediaFiles.FindAsync(mediaFile.Id);
        updatedFile!.Status.Should().Be(MediaStatus.Rejected);

        _publisherMock.Verify(x => x.Publish(
            It.Is<MediaScanFailedEvent>(e => e.MediaId == mediaFile.Id && e.Reason == "Virus detected or scan failed."),
            It.IsAny<CancellationToken>()), Times.Once);

        // Cleanup
        if (File.Exists(tempPath)) File.Delete(tempPath);
    }

    [Fact]
    public async Task Handle_HashMismatch_ShouldRejectFile()
    {
        // Arrange
        var mediaFile = MediaFile.Create("test.mp4", "original-hash", 50_000_000, "video/mp4", "test-owner-123");
        mediaFile.InitiateMultipart("upload-id-123", 3);
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var parts = new[] { new PartETagDto(1, "etag1") };
        var command = new CompleteMultipartUploadCommand(mediaFile.Id, parts);

        var tempPath = Path.Combine(Path.GetTempPath(), $"media-scan-{Guid.NewGuid()}");
        var testData = System.Text.Encoding.UTF8.GetBytes("different content that will create different hash");
        await File.WriteAllBytesAsync(tempPath, testData);

        _s3ServiceMock.Setup(x => x.DownloadToFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((key, destPath, ct) => File.Copy(tempPath, destPath, true))
            .ReturnsAsync((string key, string destPath, CancellationToken ct) => destPath);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.HashMismatch");

        var updatedFile = await _context.MediaFiles.FindAsync(mediaFile.Id);
        updatedFile!.Status.Should().Be(MediaStatus.Rejected);

        // Cleanup
        if (File.Exists(tempPath)) File.Delete(tempPath);
    }

    [Fact]
    public async Task Handle_NoUserId_ShouldReturnUnauthorized()
    {
        // Arrange
        _currentUserMock.Setup(x => x.UserId).Returns(string.Empty);
        var command = new CompleteMultipartUploadCommand(Guid.NewGuid(), new[] { new PartETagDto(1, "etag1") });

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
        var command = new CompleteMultipartUploadCommand(Guid.NewGuid(), new[] { new PartETagDto(1, "etag1") });

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
        var mediaFile = MediaFile.Create("test.mp4", "hash123", 50_000_000, "video/mp4", "other-owner");
        mediaFile.InitiateMultipart("upload-id-123", 3);
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var command = new CompleteMultipartUploadCommand(mediaFile.Id, new[] { new PartETagDto(1, "etag1") });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Forbidden");
    }

    [Fact]
    public async Task Handle_NotMultipartUpload_ShouldReturnError()
    {
        // Arrange
        var mediaFile = MediaFile.Create("test.mp4", "hash123", 1024, "video/mp4", "test-owner-123");
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var command = new CompleteMultipartUploadCommand(mediaFile.Id, new[] { new PartETagDto(1, "etag1") });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.NotMultipart");
    }

    [Fact]
    public async Task Handle_InvalidStatus_ShouldReturnError()
    {
        // Arrange
        var mediaFile = MediaFile.Create("test.mp4", "hash123", 50_000_000, "video/mp4", "test-owner-123");
        mediaFile.InitiateMultipart("upload-id-123", 3);
        mediaFile.MarkAsActive(); // Change to non-pending status
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var command = new CompleteMultipartUploadCommand(mediaFile.Id, new[] { new PartETagDto(1, "etag1") });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.InvalidState");
    }
}