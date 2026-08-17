using FluentAssertions;
using Haworks.Media.Api.Infrastructure;
using Haworks.Media.Api.Infrastructure.Processing;
using Haworks.Media.Api.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Haworks.Media.Unit;

// ImageProcessor previously had zero test references. CanProcess is pure routing logic
// exercised here directly. The decompression-bomb guard in ProcessAsync (ImageProcessor.cs:28-33)
// needs a crafted oversized image fixture and is left for an integration-tier test.
public class ImageProcessorTests
{
    private static ImageProcessor CreateProcessor(ImageOptions? opts = null) =>
        new(Mock.Of<IS3Service>(), Options.Create(opts ?? new ImageOptions()), NullLogger<ImageProcessor>.Instance);

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    public void CanProcess_RasterImageMimeType_ShouldReturnTrue(string mimeType)
    {
        var processor = CreateProcessor();
        processor.CanProcess(mimeType).Should().BeTrue();
    }

    [Fact]
    public void CanProcess_SvgMimeType_ShouldReturnFalse()
    {
        var processor = CreateProcessor();
        processor.CanProcess("image/svg+xml").Should().BeFalse();
    }

    [Theory]
    [InlineData("video/mp4")]
    [InlineData("application/pdf")]
    [InlineData("audio/mpeg")]
    public void CanProcess_NonImageMimeType_ShouldReturnFalse(string mimeType)
    {
        var processor = CreateProcessor();
        processor.CanProcess(mimeType).Should().BeFalse();
    }

    [Fact]
    public void CanProcess_WhenDisabledInOptions_ShouldReturnFalse()
    {
        var processor = CreateProcessor(new ImageOptions { Enabled = false });
        processor.CanProcess("image/png").Should().BeFalse();
    }
}
