using System.Text;
using FluentAssertions;
using Haworks.Media.Api.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Haworks.Media.Unit;

public class FileSignatureValidatorTests
{
    private readonly Mock<ILogger<FileSignatureValidator>> _loggerMock;
    private readonly FileSignatureValidator _validator;

    public FileSignatureValidatorTests()
    {
        _loggerMock = new Mock<ILogger<FileSignatureValidator>>();
        _validator = new FileSignatureValidator(_loggerMock.Object);
    }

    [Fact]
    public async Task ValidateAsync_NullStream_ReturnsFalse()
    {
        var result = await _validator.ValidateAsync(null!);

        result.IsValid.Should().BeFalse();
        result.FileType.Should().Be("Unknown");
    }

    [Fact]
    public async Task ValidateAsync_EmptyStream_ReturnsFalse()
    {
        using var stream = new MemoryStream();

        var result = await _validator.ValidateAsync(stream);

        result.IsValid.Should().BeFalse();
        result.FileType.Should().Be("Unknown");
        VerifyLogWarning("File signature validation failed — empty stream");
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")] // JPEG JFIF
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 }, "image/jpeg")] // JPEG Exif
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE2 }, "image/jpeg")] // JPEG
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE3 }, "image/jpeg")] // JPEG
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE8 }, "image/jpeg")] // JPEG SPIFF
    public async Task ValidateAsync_JpegSignatures_ReturnsJpegType(byte[] signature, string expectedType)
    {
        using var stream = new MemoryStream(signature.Concat(new byte[20]).ToArray());

        var result = await _validator.ValidateAsync(stream);

        result.IsValid.Should().BeTrue();
        result.FileType.Should().Be(expectedType);
    }

    [Fact]
    public async Task ValidateAsync_PngSignature_ReturnsPngType()
    {
        var pngSignature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        using var stream = new MemoryStream(pngSignature.Concat(new byte[20]).ToArray());

        var result = await _validator.ValidateAsync(stream);

        result.IsValid.Should().BeTrue();
        result.FileType.Should().Be("image/png");
    }

    [Theory]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 })] // GIF87a
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })] // GIF89a
    public async Task ValidateAsync_GifSignatures_ReturnsGifType(byte[] signature)
    {
        using var stream = new MemoryStream(signature.Concat(new byte[20]).ToArray());

        var result = await _validator.ValidateAsync(stream);

        result.IsValid.Should().BeTrue();
        result.FileType.Should().Be("image/gif");
    }

    [Fact]
    public async Task ValidateAsync_PdfSignature_ReturnsPdfType()
    {
        var pdfSignature = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        using var stream = new MemoryStream(pdfSignature.Concat(new byte[20]).ToArray());

        var result = await _validator.ValidateAsync(stream);

        result.IsValid.Should().BeTrue();
        result.FileType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task ValidateAsync_WebPSignature_RequiresAdditionalValidation()
    {
        // WebP requires RIFF signature + WEBP at positions 8-11
        var webpSignature = new byte[]
        {
            0x52, 0x49, 0x46, 0x46, // RIFF
            0x00, 0x00, 0x00, 0x00, // File size (placeholder)
            0x57, 0x45, 0x42, 0x50  // WEBP
        };
        using var stream = new MemoryStream(webpSignature.Concat(new byte[20]).ToArray());

        var result = await _validator.ValidateAsync(stream);

        result.IsValid.Should().BeTrue();
        result.FileType.Should().Be("image/webp");
    }

    [Fact]
    public async Task ValidateAsync_WebPIncomplete_ReturnsFalse()
    {
        // Only RIFF signature without WEBP identifier
        var incompleteWebp = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00 };
        using var stream = new MemoryStream(incompleteWebp);

        var result = await _validator.ValidateAsync(stream);

        result.IsValid.Should().BeFalse();
        result.FileType.Should().Be("Unknown");
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 })]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70 })]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70 })]
    public async Task ValidateAsync_Mp4Signatures_ReturnsMp4Type(byte[] signature)
    {
        using var stream = new MemoryStream(signature.Concat(new byte[20]).ToArray());

        var result = await _validator.ValidateAsync(stream);

        result.IsValid.Should().BeTrue();
        result.FileType.Should().Be("video/mp4");
    }

    [Theory]
    [InlineData(new byte[] { 0x49, 0x44, 0x33 })] // ID3
    [InlineData(new byte[] { 0xFF, 0xFB })] // MP3 frame sync
    [InlineData(new byte[] { 0xFF, 0xF3 })] // MP3 frame sync
    [InlineData(new byte[] { 0xFF, 0xF2 })] // MP3 frame sync
    public async Task ValidateAsync_Mp3Signatures_ReturnsMp3Type(byte[] signature)
    {
        using var stream = new MemoryStream(signature.Concat(new byte[20]).ToArray());

        var result = await _validator.ValidateAsync(stream);

        result.IsValid.Should().BeTrue();
        result.FileType.Should().Be("audio/mpeg");
    }

    [Fact]
    public async Task ValidateAsync_OggSignature_ReturnsOggType()
    {
        var oggSignature = new byte[] { 0x4F, 0x67, 0x67, 0x53 };
        using var stream = new MemoryStream(oggSignature.Concat(new byte[20]).ToArray());

        var result = await _validator.ValidateAsync(stream);

        result.IsValid.Should().BeTrue();
        result.FileType.Should().Be("audio/ogg");
    }

    [Fact]
    public async Task ValidateAsync_FlacSignature_ReturnsFlacType()
    {
        var flacSignature = new byte[] { 0x66, 0x4C, 0x61, 0x43 };
        using var stream = new MemoryStream(flacSignature.Concat(new byte[20]).ToArray());

        var result = await _validator.ValidateAsync(stream);

        result.IsValid.Should().BeTrue();
        result.FileType.Should().Be("audio/flac");
    }

    [Theory]
    [InlineData("video/webm")]
    [InlineData("video/x-matroska")]
    public async Task ValidateAsync_MatroskaEbmlSignature_ReturnsCorrectType(string expectedType)
    {
        // Both WebM and Matroska use the same EBML signature
        var ebmlSignature = new byte[] { 0x1A, 0x45, 0xDF, 0xA3 };
        using var stream = new MemoryStream(ebmlSignature.Concat(new byte[20]).ToArray());

        var result = await _validator.ValidateAsync(stream);

        result.IsValid.Should().BeTrue();
        // Note: Both types share the same signature, implementation returns first match
        result.FileType.Should().BeOneOf("video/webm", "video/x-matroska");
    }

    [Fact]
    public async Task ValidateAsync_ExecutableFile_LogsCriticalAndReturnsFalse()
    {
        var mzHeader = new byte[] { 0x4D, 0x5A }; // MZ header for Windows executables
        using var stream = new MemoryStream(mzHeader.Concat(new byte[20]).ToArray());

        var result = await _validator.ValidateAsync(stream);

        result.IsValid.Should().BeFalse();
        result.FileType.Should().Be("Unknown");
        VerifyLogCritical("Security: attempted upload of executable file (MZ header) blocked");
    }

    [Fact]
    public async Task ValidateAsync_UnknownFileType_LogsWarningAndReturnsFalse()
    {
        var unknownSignature = new byte[] { 0x12, 0x34, 0x56, 0x78 };
        using var stream = new MemoryStream(unknownSignature.Concat(new byte[20]).ToArray());

        var result = await _validator.ValidateAsync(stream);

        result.IsValid.Should().BeFalse();
        result.FileType.Should().Be("Unknown");
        VerifyLogWarning("File signature validation failed — unknown or untrusted file type");
    }

    [Fact]
    public async Task ValidateAsync_PartialSignature_HandlesShorterThanExpected()
    {
        var shortSignature = new byte[] { 0xFF, 0xD8 }; // Incomplete JPEG signature
        using var stream = new MemoryStream(shortSignature);

        var result = await _validator.ValidateAsync(stream);

        result.IsValid.Should().BeFalse();
        result.FileType.Should().Be("Unknown");
    }

    [Fact]
    public async Task ValidateAsync_LargeFile_OnlyReadsFirstTwelveBytes()
    {
        var pngSignature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var largeFile = pngSignature.Concat(new byte[1024 * 1024]).ToArray(); // 1MB+ file
        using var stream = new MemoryStream(largeFile);

        var result = await _validator.ValidateAsync(stream);

        result.IsValid.Should().BeTrue();
        result.FileType.Should().Be("image/png");
        // Verify we didn't read the entire file, just the signature
        stream.Position.Should().BeLessOrEqualTo(12);
    }

    [Fact]
    public async Task ValidateAsync_CancellationRequested_RespectsCancellation()
    {
        var pngSignature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        using var stream = new MemoryStream(pngSignature);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _validator.ValidateAsync(stream, cts.Token));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(11)]
    public async Task ValidateAsync_StreamSmallerThanSignature_HandlesTruncatedFiles(int streamSize)
    {
        var truncatedData = new byte[streamSize];
        Random.Shared.NextBytes(truncatedData);
        using var stream = new MemoryStream(truncatedData);

        var result = await _validator.ValidateAsync(stream);

        result.IsValid.Should().BeFalse();
        result.FileType.Should().Be("Unknown");
    }

    private void VerifyLogWarning(string expectedMessage)
    {
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expectedMessage)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private void VerifyLogCritical(string expectedMessage)
    {
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expectedMessage)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}