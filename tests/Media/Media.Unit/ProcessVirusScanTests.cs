using FluentAssertions;
using Haworks.BuildingBlocks.CurrentUser;
using Haworks.Media.Api.Application;
using Haworks.Media.Api.Infrastructure;
using Haworks.Media.Api.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Haworks.Media.Unit;

public class ProcessVirusScanTests
{
    private const string OwnerId = "test-owner-456";
    // SHA-256 of bytes { 0x01, 0x02, 0x03 } used in the mock S3 download
    private const string TestFileHash = "039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81";
    private readonly MediaDbContext _context;
    private readonly Mock<IVirusScanner> _scannerMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly Mock<IS3Service> _s3Mock;
    private readonly Mock<IFileSignatureValidator> _signatureValidatorMock;
    private readonly Mock<IPublishEndpoint> _publisherMock;
    private readonly ProcessVirusScanHandler _handler;

    public ProcessVirusScanTests()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _context = new MediaDbContext(options);
        _scannerMock = new Mock<IVirusScanner>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(x => x.UserId).Returns(OwnerId);
        _s3Mock = new Mock<IS3Service>();
        // DownloadToFileAsync writes test bytes to the temp file path
        _s3Mock.Setup(x => x.DownloadToFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string destPath, CancellationToken _) =>
            {
                File.WriteAllBytes(destPath, new byte[] { 1, 2, 3 });
                return Task.FromResult(destPath);
            });
        _publisherMock = new Mock<IPublishEndpoint>();
        var sendEndpointMock = new Mock<ISendEndpointProvider>();
        sendEndpointMock.Setup(x => x.GetSendEndpoint(It.IsAny<Uri>()))
            .ReturnsAsync(Mock.Of<ISendEndpoint>());
        _signatureValidatorMock = new Mock<IFileSignatureValidator>();
        _signatureValidatorMock.Setup(x => x.ValidateAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileSignatureValidationResult(true, "image/png"));
        _handler = new ProcessVirusScanHandler(
            _context, _scannerMock.Object, _signatureValidatorMock.Object,
            _currentUserMock.Object, _s3Mock.Object,
            _publisherMock.Object, sendEndpointMock.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProcessVirusScanHandler>.Instance);
    }

    [Fact]
    public async Task Handle_CleanFile_ShouldUpdateStatusToActive()
    {
        var mediaFile = MediaFile.Create("test.png", TestFileHash, 1024, "image/png", OwnerId);
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        _scannerMock.Setup(x => x.ScanFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(new ProcessVirusScanCommand(mediaFile.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var updatedFile = await _context.MediaFiles.FindAsync(mediaFile.Id);
        updatedFile!.Status.Should().Be(MediaStatus.Active);
    }

    [Fact]
    public async Task Handle_InfectedFile_ShouldUpdateStatusToRejected()
    {
        var mediaFile = MediaFile.Create("bad.exe", TestFileHash, 2048, "application/octet-stream", OwnerId);
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        _scannerMock.Setup(x => x.ScanFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(new ProcessVirusScanCommand(mediaFile.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var updatedFile = await _context.MediaFiles.FindAsync(mediaFile.Id);
        updatedFile!.Status.Should().Be(MediaStatus.Rejected);
    }

    [Fact]
    public async Task Handle_InvalidId_ShouldReturnFailure()
    {
        var result = await _handler.Handle(new ProcessVirusScanCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.NotFound");
    }

    [Fact]
    public async Task Handle_WrongOwner_ShouldReturnForbidden()
    {
        var mediaFile = MediaFile.Create("test.png", new string('e', 64), 1024, "image/png", "other-owner");
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var result = await _handler.Handle(new ProcessVirusScanCommand(mediaFile.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Forbidden");
    }

    [Fact]
    public async Task Handle_NoUserId_ShouldReturnUnauthorized()
    {
        _currentUserMock.Setup(x => x.UserId).Returns(string.Empty);

        var result = await _handler.Handle(new ProcessVirusScanCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Unauthorized");
    }

    [Fact]
    public async Task Handle_SignatureMismatch_ShouldQuarantineFileAndPublishScanFailed()
    {
        var mediaFile = MediaFile.Create("fake.png", TestFileHash, 1024, "image/png", OwnerId);
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        _signatureValidatorMock.Setup(x => x.ValidateAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileSignatureValidationResult(false, "application/octet-stream"));

        var result = await _handler.Handle(new ProcessVirusScanCommand(mediaFile.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.SignatureMismatch");
        var updatedFile = await _context.MediaFiles.FindAsync(mediaFile.Id);
        updatedFile!.Status.Should().Be(MediaStatus.Rejected);
        _s3Mock.Verify(x => x.QuarantineAsync(mediaFile.Id.ToString(), It.IsAny<CancellationToken>()), Times.Once);
        _publisherMock.Verify(x => x.Publish(
            It.Is<Haworks.Contracts.Media.MediaScanFailedEvent>(e => e.MediaId == mediaFile.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        // Virus scan must never run on a file whose declared type doesn't match its bytes
        _scannerMock.Verify(x => x.ScanFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_HashMismatch_ShouldRejectFileWithoutScanning()
    {
        // Declared hash does not match the SHA-256 of the mocked download bytes { 1, 2, 3 }
        var mediaFile = MediaFile.Create("test.png", new string('e', 64), 1024, "image/png", OwnerId);
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        var result = await _handler.Handle(new ProcessVirusScanCommand(mediaFile.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.HashMismatch");
        var updatedFile = await _context.MediaFiles.FindAsync(mediaFile.Id);
        updatedFile!.Status.Should().Be(MediaStatus.Rejected);
        _signatureValidatorMock.Verify(x => x.ValidateAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        _scannerMock.Verify(x => x.ScanFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ConcurrentScanRace_ShouldSwallowDbUpdateConcurrencyExceptionAndReturnSuccess()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var mediaFile = MediaFile.Create("race.png", TestFileHash, 1024, "image/png", OwnerId);
        await using (var seedContext = new MediaDbContext(options))
        {
            seedContext.MediaFiles.Add(mediaFile);
            await seedContext.SaveChangesAsync();
        }

        // Simulates another consumer instance having already committed the status change
        // to Quarantined/Active before this one's SaveChangesAsync — a legitimate race
        // the handler's catch block is designed to swallow, not crash on.
        await using var raceContext = new ThrowingSaveChangesDbContext(options, throwOnCall: 1);
        _scannerMock.Setup(x => x.ScanFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var handler = new ProcessVirusScanHandler(
            raceContext, _scannerMock.Object, _signatureValidatorMock.Object,
            _currentUserMock.Object, _s3Mock.Object,
            _publisherMock.Object, Mock.Of<ISendEndpointProvider>(p =>
                p.GetSendEndpoint(It.IsAny<Uri>()) == Task.FromResult(Mock.Of<ISendEndpoint>())),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProcessVirusScanHandler>.Instance);

        var result = await handler.Handle(new ProcessVirusScanCommand(mediaFile.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    private sealed class ThrowingSaveChangesDbContext(DbContextOptions<MediaDbContext> options, int throwOnCall) : MediaDbContext(options)
    {
        private int _callCount;

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _callCount++;
            if (_callCount == throwOnCall)
                throw new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("Simulated concurrency conflict.");
            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
