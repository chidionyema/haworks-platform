using FluentAssertions;
using Haworks.Media.Api.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Haworks.Media.Unit;

// The real magic-byte validation logic was previously only exercised via a mock
// (ProcessVirusScanTests). These tests run the actual FileSignatureValidator implementation.
public class FileSignatureValidatorTests
{
    private readonly FileSignatureValidator _validator = new(NullLogger<FileSignatureValidator>.Instance);

    private static Stream StreamOf(params byte[] bytes) => new MemoryStream(bytes);

    [Fact]
    public async Task ValidateAsync_PngMagicBytes_ShouldReturnValidPng()
    {
        var result = await _validator.ValidateAsync(StreamOf(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0));
        result.IsValid.Should().BeTrue();
        result.FileType.Should().Be("image/png");
    }

    [Fact]
    public async Task ValidateAsync_JpegMagicBytes_ShouldReturnValidJpeg()
    {
        var result = await _validator.ValidateAsync(StreamOf(0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0));
        result.IsValid.Should().BeTrue();
        result.FileType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task ValidateAsync_PdfMagicBytes_ShouldReturnValidPdf()
    {
        var result = await _validator.ValidateAsync(StreamOf(0x25, 0x50, 0x44, 0x46, 0x2D, 0, 0, 0, 0, 0, 0, 0));
        result.IsValid.Should().BeTrue();
        result.FileType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task ValidateAsync_WebpWithCorrectRiffAndWebpMarker_ShouldReturnValidWebp()
    {
        var result = await _validator.ValidateAsync(StreamOf(
            0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50));
        result.IsValid.Should().BeTrue();
        result.FileType.Should().Be("image/webp");
    }

    [Fact]
    public async Task ValidateAsync_RiffWithoutWebpMarker_ShouldReturnInvalid()
    {
        // A WAV file also starts with a RIFF header but is not WEBP at bytes 8-11
        var result = await _validator.ValidateAsync(StreamOf(
            0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45));
        result.IsValid.Should().BeFalse();
        result.FileType.Should().Be("Unknown");
    }

    [Fact]
    public async Task ValidateAsync_UnknownBytes_ShouldReturnInvalid()
    {
        var result = await _validator.ValidateAsync(StreamOf(0x01, 0x02, 0x03, 0x04));
        result.IsValid.Should().BeFalse();
        result.FileType.Should().Be("Unknown");
    }

    [Fact]
    public async Task ValidateAsync_EmptyStream_ShouldReturnInvalid()
    {
        var result = await _validator.ValidateAsync(StreamOf());
        result.IsValid.Should().BeFalse();
        result.FileType.Should().Be("Unknown");
    }

    [Fact]
    public async Task ValidateAsync_ExecutableMzHeader_ShouldBeRejected()
    {
        // Windows PE/EXE files start with "MZ" (0x4D, 0x5A) — must never validate as any allowed type
        var result = await _validator.ValidateAsync(StreamOf(0x4D, 0x5A, 0x90, 0x00, 0, 0, 0, 0, 0, 0, 0, 0));
        result.IsValid.Should().BeFalse();
        result.FileType.Should().Be("Unknown");
    }

    [Fact]
    public async Task ValidateAsync_NullStream_ShouldReturnInvalid()
    {
        var result = await _validator.ValidateAsync(null!);
        result.IsValid.Should().BeFalse();
        result.FileType.Should().Be("Unknown");
    }
}
