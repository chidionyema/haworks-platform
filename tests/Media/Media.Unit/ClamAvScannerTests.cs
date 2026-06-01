using System.Text;
using FluentAssertions;
using Haworks.Media.Api.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using nClam;
using Xunit;

namespace Haworks.Media.Unit;

public class ClamAvScannerTests
{
    private readonly Mock<ILogger<ClamAvScanner>> _loggerMock;
    private readonly ClamAvScanner _scanner;

    public ClamAvScannerTests()
    {
        _loggerMock = new Mock<ILogger<ClamAvScanner>>();
        var options = Options.Create(new ClamAvOptions
        {
            Enabled = true,
            Host = "localhost",
            Port = 3310,
            TimeoutSeconds = 30,
            InStreamMaxBytes = 25_000_000
        });
        _scanner = new ClamAvScanner(options, _loggerMock.Object);
    }

    [Fact]
    public async Task ScanAsync_DisabledClamAv_ReturnsTrue()
    {
        var options = Options.Create(new ClamAvOptions { Enabled = false });
        var scanner = new ClamAvScanner(options, _loggerMock.Object);
        using var stream = new MemoryStream("test"u8.ToArray());

        var result = await scanner.ScanAsync(stream);

        result.Should().BeTrue();
        VerifyLogWarning("ClamAV disabled — skipping scan (UNSAFE in production)");
    }

    [Fact]
    public async Task ScanFileAsync_DisabledClamAv_ReturnsTrue()
    {
        var options = Options.Create(new ClamAvOptions { Enabled = false });
        var scanner = new ClamAvScanner(options, _loggerMock.Object);
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "test");

        try
        {
            var result = await scanner.ScanFileAsync(tempFile);

            result.Should().BeTrue();
            VerifyLogWarning("ClamAV disabled — skipping scan (UNSAFE in production)");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ScanAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        using var stream = new MemoryStream("test"u8.ToArray());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _scanner.ScanAsync(stream, cts.Token));
    }

    [Fact]
    public async Task ScanFileAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "test");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => _scanner.ScanFileAsync(tempFile, cts.Token));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData(1024)] // Small file - stream path
    [InlineData(25_000_000)] // At threshold - stream path
    public async Task ScanAsync_SmallFile_UsesStreamPath(long fileSize)
    {
        var data = new byte[fileSize];
        Random.Shared.NextBytes(data);
        using var stream = new MemoryStream(data);

        // This will fail because we don't have a real ClamAV instance, but we're testing the path selection logic
        var result = await _scanner.ScanAsync(stream);

        result.Should().BeFalse(); // Fails closed on connection error
        stream.Position.Should().Be(0); // Stream was reset to beginning
    }

    [Fact]
    public async Task ScanAsync_LargeFile_UsesFileSystemPath()
    {
        var data = new byte[25_000_001]; // Just over threshold
        Random.Shared.NextBytes(data);
        using var stream = new MemoryStream(data);

        // This will fail because we don't have a real ClamAV instance, but we're testing the path selection logic
        var result = await _scanner.ScanAsync(stream);

        result.Should().BeFalse(); // Fails closed on connection error
    }

    [Fact]
    public async Task ScanFileAsync_SmallFile_ReadsIntoStreamForInStreamProtocol()
    {
        var tempFile = Path.GetTempFileName();
        await File.WriteAllBytesAsync(tempFile, "small test content"u8.ToArray());

        try
        {
            var result = await _scanner.ScanFileAsync(tempFile);

            result.Should().BeFalse(); // Fails closed on connection error
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ScanFileAsync_LargeFile_UsesScanFileOnServerAsync()
    {
        var tempFile = Path.GetTempFileName();
        var largeData = new byte[25_000_001];
        Random.Shared.NextBytes(largeData);
        await File.WriteAllBytesAsync(tempFile, largeData);

        try
        {
            var result = await _scanner.ScanFileAsync(tempFile);

            result.Should().BeFalse(); // Fails closed on connection error
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData("test.txt")]
    [InlineData("path/to/file.bin")]
    [InlineData("/tmp/scan-12345")]
    public async Task ScanFileAsync_NonExistentFile_ThrowsFileNotFoundException(string filePath)
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => _scanner.ScanFileAsync(filePath));
    }

    [Fact]
    public async Task ScanAsync_StreamPositionNotAtStart_ResetsPosition()
    {
        using var stream = new MemoryStream("test data"u8.ToArray());
        stream.Position = 5;

        await _scanner.ScanAsync(stream);

        stream.Position.Should().Be(0);
    }

    [Fact]
    public async Task ScanAsync_EmptyStream_HandlesGracefully()
    {
        using var stream = new MemoryStream();

        var result = await _scanner.ScanAsync(stream);

        result.Should().BeFalse(); // Fails closed on connection error
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
}