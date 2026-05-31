using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Haworks.Media.Api.Infrastructure;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Haworks.Media.Unit;

public class S3ServiceTests
{
    private readonly Mock<IAmazonS3> _s3ClientMock;
    private readonly MediaStorageOptions _enabledOptions;
    private readonly MediaStorageOptions _disabledOptions;

    public S3ServiceTests()
    {
        _s3ClientMock = new Mock<IAmazonS3>();

        _enabledOptions = new MediaStorageOptions
        {
            Enabled = true,
            BucketName = "test-bucket",
            ServiceUrl = "https://s3.amazonaws.com",
            PresignedUrlExpiryMinutes = 60
        };

        _disabledOptions = new MediaStorageOptions
        {
            Enabled = false,
            BucketName = "test-bucket",
            ServiceUrl = "https://s3.amazonaws.com",
            PresignedUrlExpiryMinutes = 60
        };
    }

    [Fact]
    public void GeneratePreSignedUrl_WhenEnabled_ReturnsValidUrl()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_enabledOptions));

        _s3ClientMock.Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
            .Returns("https://test-bucket.s3.amazonaws.com/test-key?signature=abc123");

        // Act
        var result = service.GeneratePreSignedUrl("test-key", "image/png");

        // Assert
        result.Should().Be("https://test-bucket.s3.amazonaws.com/test-key?signature=abc123");

        _s3ClientMock.Verify(x => x.GetPreSignedURL(It.Is<GetPreSignedUrlRequest>(req =>
            req.BucketName == "test-bucket" &&
            req.Key == "test-key" &&
            req.Verb == HttpVerb.PUT &&
            req.ContentType == "image/png" &&
            req.Protocol == Protocol.HTTPS)), Times.Once);
    }

    [Fact]
    public void GeneratePreSignedUrl_WhenDisabled_ReturnsDisabledUrl()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_disabledOptions));

        // Act
        var result = service.GeneratePreSignedUrl("test-key", "image/png");

        // Assert
        result.Should().Be("https://s3-disabled.local/test-bucket/test-key");

        _s3ClientMock.Verify(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()), Times.Never);
    }

    [Fact]
    public void GeneratePreSignedUrl_WithHttpServiceUrl_UsesHttpProtocol()
    {
        // Arrange
        var httpOptions = new MediaStorageOptions
        {
            Enabled = true,
            BucketName = "test-bucket",
            ServiceUrl = "http://localhost:4566", // LocalStack
            PresignedUrlExpiryMinutes = 60
        };
        var service = new S3Service(_s3ClientMock.Object, Options.Create(httpOptions));

        _s3ClientMock.Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
            .Returns("http://localhost:4566/test-bucket/test-key?signature=abc123");

        // Act
        var result = service.GeneratePreSignedUrl("test-key", "image/png");

        // Assert
        result.Should().Be("http://localhost:4566/test-bucket/test-key?signature=abc123");

        _s3ClientMock.Verify(x => x.GetPreSignedURL(It.Is<GetPreSignedUrlRequest>(req =>
            req.Protocol == Protocol.HTTP)), Times.Once);
    }

    [Fact]
    public async Task InitiateMultipartUploadAsync_WhenEnabled_ReturnsUploadId()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_enabledOptions));

        _s3ClientMock.Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitiateMultipartUploadResponse { UploadId = "upload-123" });

        // Act
        var result = await service.InitiateMultipartUploadAsync("test-key", "video/mp4", CancellationToken.None);

        // Assert
        result.Should().Be("upload-123");

        _s3ClientMock.Verify(x => x.InitiateMultipartUploadAsync(
            It.Is<InitiateMultipartUploadRequest>(req =>
                req.BucketName == "test-bucket" &&
                req.Key == "test-key" &&
                req.ContentType == "video/mp4"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitiateMultipartUploadAsync_WhenDisabled_ReturnsDisabledId()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_disabledOptions));

        // Act
        var result = await service.InitiateMultipartUploadAsync("test-key", "video/mp4", CancellationToken.None);

        // Assert
        result.Should().StartWith("disabled-upload-");
        result.Should().HaveLength("disabled-upload-".Length + 36); // GUID length

        _s3ClientMock.Verify(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void GeneratePartPresignedUrl_WhenEnabled_ReturnsValidUrl()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_enabledOptions));

        _s3ClientMock.Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
            .Returns("https://test-bucket.s3.amazonaws.com/test-key?partNumber=1&uploadId=upload-123&signature=abc123");

        // Act
        var result = service.GeneratePartPresignedUrl("test-key", "upload-123", 1);

        // Assert
        result.Should().Be("https://test-bucket.s3.amazonaws.com/test-key?partNumber=1&uploadId=upload-123&signature=abc123");

        _s3ClientMock.Verify(x => x.GetPreSignedURL(It.Is<GetPreSignedUrlRequest>(req =>
            req.BucketName == "test-bucket" &&
            req.Key == "test-key" &&
            req.UploadId == "upload-123" &&
            req.PartNumber == 1 &&
            req.Verb == HttpVerb.PUT)), Times.Once);
    }

    [Fact]
    public void GeneratePartPresignedUrl_WhenDisabled_ReturnsDisabledUrl()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_disabledOptions));

        // Act
        var result = service.GeneratePartPresignedUrl("test-key", "upload-123", 1);

        // Assert
        result.Should().Be("https://s3-disabled.local/test-bucket/test-key?partNumber=1&uploadId=upload-123");
    }

    [Fact]
    public async Task CompleteMultipartUploadAsync_WhenEnabled_CallsS3()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_enabledOptions));
        var parts = new List<PartETag>
        {
            new() { PartNumber = 1, ETag = "etag1" },
            new() { PartNumber = 2, ETag = "etag2" }
        };

        _s3ClientMock.Setup(x => x.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompleteMultipartUploadResponse());

        // Act
        await service.CompleteMultipartUploadAsync("test-key", "upload-123", parts, CancellationToken.None);

        // Assert
        _s3ClientMock.Verify(x => x.CompleteMultipartUploadAsync(
            It.Is<CompleteMultipartUploadRequest>(req =>
                req.BucketName == "test-bucket" &&
                req.Key == "test-key" &&
                req.UploadId == "upload-123" &&
                req.PartETags.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteMultipartUploadAsync_WhenDisabled_DoesNotCallS3()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_disabledOptions));
        var parts = new List<PartETag> { new() { PartNumber = 1, ETag = "etag1" } };

        // Act
        await service.CompleteMultipartUploadAsync("test-key", "upload-123", parts, CancellationToken.None);

        // Assert
        _s3ClientMock.Verify(x => x.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AbortMultipartUploadAsync_WhenEnabled_CallsS3()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_enabledOptions));

        _s3ClientMock.Setup(x => x.AbortMultipartUploadAsync(It.IsAny<AbortMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AbortMultipartUploadResponse());

        // Act
        await service.AbortMultipartUploadAsync("test-key", "upload-123", CancellationToken.None);

        // Assert
        _s3ClientMock.Verify(x => x.AbortMultipartUploadAsync(
            It.Is<AbortMultipartUploadRequest>(req =>
                req.BucketName == "test-bucket" &&
                req.Key == "test-key" &&
                req.UploadId == "upload-123"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadAsync_WhenEnabled_CallsS3()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_enabledOptions));
        var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test content"));

        _s3ClientMock.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse());

        // Act
        await service.UploadAsync("test-key", "text/plain", content, CancellationToken.None);

        // Assert
        _s3ClientMock.Verify(x => x.PutObjectAsync(
            It.Is<PutObjectRequest>(req =>
                req.BucketName == "test-bucket" &&
                req.Key == "test-key" &&
                req.ContentType == "text/plain"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void GeneratePresignedGetUrl_WhenEnabled_ReturnsValidUrl()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_enabledOptions));

        _s3ClientMock.Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
            .Returns("https://test-bucket.s3.amazonaws.com/test-key?signature=get123");

        // Act
        var result = service.GeneratePresignedGetUrl("test-key");

        // Assert
        result.Should().Be("https://test-bucket.s3.amazonaws.com/test-key?signature=get123");

        _s3ClientMock.Verify(x => x.GetPreSignedURL(It.Is<GetPreSignedUrlRequest>(req =>
            req.BucketName == "test-bucket" &&
            req.Key == "test-key" &&
            req.Verb == HttpVerb.GET)), Times.Once);
    }

    [Fact]
    public void GeneratePresignedGetUrl_WhenDisabled_ReturnsDisabledUrl()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_disabledOptions));

        // Act
        var result = service.GeneratePresignedGetUrl("test-key");

        // Assert
        result.Should().Be("https://s3-disabled.local/test-bucket/test-key");
    }

    [Fact]
    public async Task DownloadAsync_WhenEnabled_ReturnsStream()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_enabledOptions));
        var testContent = "test file content";
        var responseStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(testContent));

        _s3ClientMock.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse { ResponseStream = responseStream });

        // Act
        var result = await service.DownloadAsync("test-key", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        using var reader = new StreamReader(result);
        var content = await reader.ReadToEndAsync();
        content.Should().Be(testContent);

        _s3ClientMock.Verify(x => x.GetObjectAsync(
            It.Is<GetObjectRequest>(req =>
                req.BucketName == "test-bucket" &&
                req.Key == "test-key"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DownloadToFileAsync_WhenEnabled_CreatesFile()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_enabledOptions));
        var testContent = "test file content for download";
        var responseStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(testContent));
        var destinationPath = Path.Combine(Path.GetTempPath(), $"test-download-{Guid.NewGuid()}.txt");

        _s3ClientMock.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse { ResponseStream = responseStream });

        try
        {
            // Act
            var result = await service.DownloadToFileAsync("test-key", destinationPath, CancellationToken.None);

            // Assert
            result.Should().Be(destinationPath);
            File.Exists(destinationPath).Should().BeTrue();

            var fileContent = await File.ReadAllTextAsync(destinationPath);
            fileContent.Should().Be(testContent);
        }
        finally
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }
        }
    }

    [Fact]
    public async Task QuarantineAsync_WhenEnabled_CopiesAndDeletes()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_enabledOptions));

        _s3ClientMock.Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopyObjectResponse());

        _s3ClientMock.Setup(x => x.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());

        // Act
        await service.QuarantineAsync("test-key", CancellationToken.None);

        // Assert
        _s3ClientMock.Verify(x => x.CopyObjectAsync(
            It.Is<CopyObjectRequest>(req =>
                req.SourceBucket == "test-bucket" &&
                req.SourceKey == "test-key" &&
                req.DestinationBucket == "test-bucket" &&
                req.DestinationKey == "quarantine/test-key"),
            It.IsAny<CancellationToken>()), Times.Once);

        _s3ClientMock.Verify(x => x.DeleteObjectAsync("test-bucket", "test-key", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PromoteFromQuarantineAsync_WhenEnabled_CopiesAndDeletes()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_enabledOptions));

        _s3ClientMock.Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopyObjectResponse());

        _s3ClientMock.Setup(x => x.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());

        // Act
        await service.PromoteFromQuarantineAsync("test-key", CancellationToken.None);

        // Assert
        _s3ClientMock.Verify(x => x.CopyObjectAsync(
            It.Is<CopyObjectRequest>(req =>
                req.SourceBucket == "test-bucket" &&
                req.SourceKey == "quarantine/test-key" &&
                req.DestinationBucket == "test-bucket" &&
                req.DestinationKey == "test-key"),
            It.IsAny<CancellationToken>()), Times.Once);

        _s3ClientMock.Verify(x => x.DeleteObjectAsync("test-bucket", "quarantine/test-key", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ComputeSha256Async_WhenEnabled_ReturnsCorrectHash()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_enabledOptions));
        var testContent = "test content for hashing";
        var expectedHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(testContent)));
        var responseStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(testContent));

        _s3ClientMock.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse { ResponseStream = responseStream });

        // Act
        var result = await service.ComputeSha256Async("test-key", CancellationToken.None);

        // Assert
        result.Should().Be(expectedHash);
    }

    [Fact]
    public async Task DeleteAsync_WhenEnabled_CallsS3()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_enabledOptions));

        _s3ClientMock.Setup(x => x.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());

        // Act
        await service.DeleteAsync("test-key", CancellationToken.None);

        // Assert
        _s3ClientMock.Verify(x => x.DeleteObjectAsync("test-bucket", "test-key", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WhenDisabled_DoesNotCallS3()
    {
        // Arrange
        var service = new S3Service(_s3ClientMock.Object, Options.Create(_disabledOptions));

        // Act
        await service.DeleteAsync("test-key", CancellationToken.None);

        // Assert
        _s3ClientMock.Verify(x => x.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}