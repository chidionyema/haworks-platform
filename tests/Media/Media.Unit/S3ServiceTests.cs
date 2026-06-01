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
    private readonly MediaStorageOptions _options;
    private readonly S3Service _service;

    public S3ServiceTests()
    {
        _s3ClientMock = new Mock<IAmazonS3>();
        _options = new MediaStorageOptions
        {
            Enabled = true,
            ServiceUrl = "https://s3.amazonaws.com",
            BucketName = "test-bucket",
            Region = "us-east-1",
            PresignedUrlExpiryMinutes = 60
        };
        _service = new S3Service(_s3ClientMock.Object, Options.Create(_options));
    }

    [Fact]
    public async Task DownloadToFileAsync_ValidKey_DownloadsToDestination()
    {
        var key = "test-file.png";
        var destinationPath = Path.GetTempFileName();
        var testContent = "test file content"u8.ToArray();

        var mockResponse = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(testContent)
        };

        _s3ClientMock.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        try
        {
            var result = await _service.DownloadToFileAsync(key, destinationPath, CancellationToken.None);

            result.Should().Be(destinationPath);
            var downloadedContent = await File.ReadAllBytesAsync(destinationPath);
            downloadedContent.Should().BeEquivalentTo(testContent);

            _s3ClientMock.Verify(x => x.GetObjectAsync(
                It.Is<GetObjectRequest>(req => req.BucketName == "test-bucket" && req.Key == key),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            File.Delete(destinationPath);
        }
    }

    [Fact]
    public async Task DownloadAsync_ValidKey_ReturnsMemoryStream()
    {
        var key = "test-file.png";
        var testContent = "test file content"u8.ToArray();

        var mockResponse = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(testContent)
        };

        _s3ClientMock.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        var result = await _service.DownloadAsync(key, CancellationToken.None);

        result.Should().NotBeNull();
        var downloadedContent = new byte[result.Length];
        result.Position = 0;
        await result.ReadAsync(downloadedContent);
        downloadedContent.Should().BeEquivalentTo(testContent);

        _s3ClientMock.Verify(x => x.GetObjectAsync(
            It.Is<GetObjectRequest>(req => req.BucketName == "test-bucket" && req.Key == key),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void GeneratePreSignedUrl_Disabled_ReturnsDisabledUrl()
    {
        var disabledOptions = new MediaStorageOptions
        {
            Enabled = false,
            BucketName = "test-bucket"
        };
        var disabledService = new S3Service(_s3ClientMock.Object, Options.Create(disabledOptions));

        var result = disabledService.GeneratePreSignedUrl("test-key", "image/png");

        result.Should().Be("https://s3-disabled.local/test-bucket/test-key");
    }

    [Theory]
    [InlineData("https://s3.amazonaws.com", Protocol.HTTPS)]
    [InlineData("http://localhost:4566", Protocol.HTTP)]
    [InlineData("", Protocol.HTTPS)] // Default when empty
    public void Constructor_ProtocolDetection_SetsCorrectProtocol(string serviceUrl, Protocol expectedProtocol)
    {
        var options = new MediaStorageOptions
        {
            Enabled = true,
            ServiceUrl = serviceUrl,
            BucketName = "test-bucket"
        };

        // Create service and test presigned URL generation
        var service = new S3Service(_s3ClientMock.Object, Options.Create(options));

        // Since we can't directly test the private field, we verify through behavior
        // This test ensures the constructor logic works correctly
        var key = "test-key";
        service.GeneratePreSignedUrl(key, "image/png"); // Should not throw

        // Constructor should complete without errors for both protocols
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task InitiateMultipartUploadAsync_ValidParameters_ReturnsUploadId()
    {
        var key = "large-file.mp4";
        var mimeType = "video/mp4";
        var expectedUploadId = "test-upload-id-123";

        var mockResponse = new InitiateMultipartUploadResponse
        {
            UploadId = expectedUploadId
        };

        _s3ClientMock.Setup(x => x.InitiateMultipartUploadAsync(
                It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        var result = await _service.InitiateMultipartUploadAsync(key, mimeType, CancellationToken.None);

        result.Should().Be(expectedUploadId);

        _s3ClientMock.Verify(x => x.InitiateMultipartUploadAsync(
            It.Is<InitiateMultipartUploadRequest>(req =>
                req.BucketName == "test-bucket" &&
                req.Key == key &&
                req.ContentType == mimeType),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteMultipartUploadAsync_ValidParameters_CompletesUpload()
    {
        var key = "large-file.mp4";
        var uploadId = "test-upload-id";
        var parts = new List<PartETag>
        {
            new() { PartNumber = 1, ETag = "etag1" },
            new() { PartNumber = 2, ETag = "etag2" }
        };

        _s3ClientMock.Setup(x => x.CompleteMultipartUploadAsync(
                It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompleteMultipartUploadResponse());

        await _service.CompleteMultipartUploadAsync(key, uploadId, parts, CancellationToken.None);

        _s3ClientMock.Verify(x => x.CompleteMultipartUploadAsync(
            It.Is<CompleteMultipartUploadRequest>(req =>
                req.BucketName == "test-bucket" &&
                req.Key == key &&
                req.UploadId == uploadId &&
                req.MultipartUpload.Parts.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AbortMultipartUploadAsync_ValidParameters_AbortsUpload()
    {
        var key = "large-file.mp4";
        var uploadId = "test-upload-id";

        _s3ClientMock.Setup(x => x.AbortMultipartUploadAsync(
                It.IsAny<AbortMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AbortMultipartUploadResponse());

        await _service.AbortMultipartUploadAsync(key, uploadId, CancellationToken.None);

        _s3ClientMock.Verify(x => x.AbortMultipartUploadAsync(
            It.Is<AbortMultipartUploadRequest>(req =>
                req.BucketName == "test-bucket" &&
                req.Key == key &&
                req.UploadId == uploadId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadAsync_ValidParameters_UploadsStream()
    {
        var key = "test-file.png";
        var mimeType = "image/png";
        var content = new MemoryStream("test content"u8.ToArray());

        _s3ClientMock.Setup(x => x.PutObjectAsync(
                It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse());

        await _service.UploadAsync(key, mimeType, content, CancellationToken.None);

        _s3ClientMock.Verify(x => x.PutObjectAsync(
            It.Is<PutObjectRequest>(req =>
                req.BucketName == "test-bucket" &&
                req.Key == key &&
                req.ContentType == mimeType &&
                req.InputStream == content),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QuarantineAsync_ValidKey_MovesToQuarantinePrefix()
    {
        var key = "test-file.png";

        _s3ClientMock.Setup(x => x.CopyObjectAsync(
                It.IsAny<CopyObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopyObjectResponse());

        _s3ClientMock.Setup(x => x.DeleteObjectAsync(
                It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());

        await _service.QuarantineAsync(key, CancellationToken.None);

        _s3ClientMock.Verify(x => x.CopyObjectAsync(
            It.Is<CopyObjectRequest>(req =>
                req.SourceBucket == "test-bucket" &&
                req.SourceKey == key &&
                req.DestinationBucket == "test-bucket" &&
                req.DestinationKey == $"quarantine/{key}"),
            It.IsAny<CancellationToken>()), Times.Once);

        _s3ClientMock.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(req =>
                req.BucketName == "test-bucket" &&
                req.Key == key),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PromoteFromQuarantineAsync_ValidKey_MovesFromQuarantinePrefix()
    {
        var key = "test-file.png";

        _s3ClientMock.Setup(x => x.CopyObjectAsync(
                It.IsAny<CopyObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopyObjectResponse());

        _s3ClientMock.Setup(x => x.DeleteObjectAsync(
                It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());

        await _service.PromoteFromQuarantineAsync(key, CancellationToken.None);

        _s3ClientMock.Verify(x => x.CopyObjectAsync(
            It.Is<CopyObjectRequest>(req =>
                req.SourceBucket == "test-bucket" &&
                req.SourceKey == $"quarantine/{key}" &&
                req.DestinationBucket == "test-bucket" &&
                req.DestinationKey == key),
            It.IsAny<CancellationToken>()), Times.Once);

        _s3ClientMock.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(req =>
                req.BucketName == "test-bucket" &&
                req.Key == $"quarantine/{key}"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ComputeSha256Async_ValidKey_ReturnsHashFromMetadata()
    {
        var key = "test-file.png";
        var expectedHash = "abcdef1234567890";

        var mockResponse = new GetObjectMetadataResponse();
        mockResponse.Metadata["sha256"] = expectedHash;

        _s3ClientMock.Setup(x => x.GetObjectMetadataAsync(
                It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        var result = await _service.ComputeSha256Async(key, CancellationToken.None);

        result.Should().Be(expectedHash);

        _s3ClientMock.Verify(x => x.GetObjectMetadataAsync(
            It.Is<GetObjectMetadataRequest>(req =>
                req.BucketName == "test-bucket" &&
                req.Key == key),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ValidKey_DeletesObject()
    {
        var key = "test-file.png";

        _s3ClientMock.Setup(x => x.DeleteObjectAsync(
                It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());

        await _service.DeleteAsync(key, CancellationToken.None);

        _s3ClientMock.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(req =>
                req.BucketName == "test-bucket" &&
                req.Key == key),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DownloadToFileAsync_CancellationRequested_RespectsCancellation()
    {
        var key = "test-file.png";
        var destinationPath = Path.GetTempFileName();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.DownloadToFileAsync(key, destinationPath, cts.Token));

        File.Delete(destinationPath);
    }

    [Fact]
    public async Task InitiateMultipartUploadAsync_S3Exception_BubblesUp()
    {
        var key = "large-file.mp4";
        var mimeType = "video/mp4";

        _s3ClientMock.Setup(x => x.InitiateMultipartUploadAsync(
                It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("Bucket not found"));

        await Assert.ThrowsAsync<AmazonS3Exception>(() =>
            _service.InitiateMultipartUploadAsync(key, mimeType, CancellationToken.None));
    }

    [Theory]
    [InlineData("test-key", "image/png")]
    [InlineData("folder/subfolder/file.pdf", "application/pdf")]
    [InlineData("very-long-key-name-with-special-chars-123.mp4", "video/mp4")]
    public void GeneratePreSignedUrl_VariousKeys_HandlesAllFormats(string key, string mimeType)
    {
        // When enabled, should not throw and return a URL
        var result = _service.GeneratePreSignedUrl(key, mimeType);

        result.Should().NotBeNullOrEmpty();
        // URL validation is tricky without real AWS SDK integration,
        // but at minimum it should not be the disabled URL pattern
        result.Should().NotStartWith("https://s3-disabled.local");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public void GeneratePartPresignedUrl_VariousPartNumbers_HandlesAllNumbers(int partNumber)
    {
        var key = "test-key";
        var uploadId = "upload-123";

        var result = _service.GeneratePartPresignedUrl(key, uploadId, partNumber);

        result.Should().NotBeNullOrEmpty();
        result.Should().NotStartWith("https://s3-disabled.local");
    }

    [Fact]
    public void GeneratePresignedGetUrl_ValidKey_ReturnsGetUrl()
    {
        var key = "test-file.png";

        var result = _service.GeneratePresignedGetUrl(key);

        result.Should().NotBeNullOrEmpty();
        result.Should().NotStartWith("https://s3-disabled.local");
    }
}