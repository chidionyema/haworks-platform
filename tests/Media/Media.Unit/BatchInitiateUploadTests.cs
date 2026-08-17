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
    private const string OwnerId = "batch-owner-1";
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
        _s3ServiceMock.Setup(x => x.GeneratePreSignedUrl(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("http://upload-url");
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(x => x.UserId).Returns(OwnerId);
        var uploadOpts = Options.Create(new UploadOptions());
        _handler = new BatchInitiateUploadHandler(_context, _s3ServiceMock.Object, _currentUserMock.Object, uploadOpts);
    }

    [Fact]
    public async Task Handle_MultipleNewFiles_ShouldCreateAllAndReturnUrls()
    {
        var command = new BatchInitiateUploadCommand([
            new InitiateUploadCommand("a.png", new string('a', 64), 1024, "image/png"),
            new InitiateUploadCommand("b.png", new string('b', 64), 2048, "image/png"),
        ]);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().OnlyContain(r => r.UploadUrl == "http://upload-url" && !r.AlreadyExists);
        (await _context.MediaFiles.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Handle_DuplicateHashAmongFiles_ShouldReturnExistingIdForDuplicate()
    {
        var hash = new string('c', 64);
        var existingFile = MediaFile.Create("old.png", hash, 512, "image/png", OwnerId);
        _context.MediaFiles.Add(existingFile);
        await _context.SaveChangesAsync();

        var command = new BatchInitiateUploadCommand([
            new InitiateUploadCommand("new.png", hash, 1024, "image/png"),
            new InitiateUploadCommand("fresh.png", new string('d', 64), 1024, "image/png"),
        ]);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].AlreadyExists.Should().BeTrue();
        result.Value[0].Id.Should().Be(existingFile.Id);
        result.Value[1].AlreadyExists.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_FileLargerThanSinglePutMax_ShouldInitiateMultipartForThatFile()
    {
        _s3ServiceMock.Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("upload-id-batch");
        _s3ServiceMock.Setup(x => x.GeneratePartPresignedUrl(It.IsAny<string>(), "upload-id-batch", It.IsAny<int>()))
            .Returns("http://part-url");

        var command = new BatchInitiateUploadCommand([
            new InitiateUploadCommand("big.mp4", new string('e', 64), 50_000_000, "video/mp4"),
        ]);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].IsMultipart.Should().BeTrue();
        result.Value[0].S3UploadId.Should().Be("upload-id-batch");
        result.Value[0].PartCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_NoUserId_ShouldReturnUnauthorized()
    {
        _currentUserMock.Setup(x => x.UserId).Returns(string.Empty);

        var command = new BatchInitiateUploadCommand([
            new InitiateUploadCommand("a.png", new string('a', 64), 1024, "image/png"),
        ]);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Unauthorized");
    }
}
