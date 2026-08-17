using FluentAssertions;
using Haworks.BuildingBlocks.CurrentUser;
using Haworks.Media.Api.Application;
using Haworks.Media.Api.Domain;
using Haworks.Media.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Haworks.Media.Unit;

public class GetMediaUrlTests
{
    private const string OwnerId = "url-owner-1";
    private readonly MediaDbContext _context;
    private readonly Mock<IS3Service> _s3Mock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly GetMediaUrlHandler _handler;

    public GetMediaUrlTests()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new MediaDbContext(options);
        _s3Mock = new Mock<IS3Service>();
        _s3Mock.Setup(x => x.GeneratePresignedGetUrl(It.IsAny<string>())).Returns("http://get-url");
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(x => x.UserId).Returns(OwnerId);
        _handler = new GetMediaUrlHandler(_context, _s3Mock.Object, _currentUserMock.Object);
    }

    private async Task<MediaFile> SeedActiveFileAsync()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);
        file.MarkAsQuarantined();
        file.MarkAsActive();
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();
        return file;
    }

    [Fact]
    public async Task Handle_ActiveOwnedFile_ShouldReturnUrl()
    {
        var file = await SeedActiveFileAsync();

        var result = await _handler.Handle(new GetMediaUrlQuery(file.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Url.Should().Be("http://get-url");
    }

    [Fact]
    public async Task Handle_NonActiveFile_ShouldReturnNotReadyFailure()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var result = await _handler.Handle(new GetMediaUrlQuery(file.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.NotReady");
    }

    [Fact]
    public async Task Handle_NotFound_ShouldReturnFailure()
    {
        var result = await _handler.Handle(new GetMediaUrlQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.NotFound");
    }

    [Fact]
    public async Task Handle_WrongOwner_ShouldReturnForbidden()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "other-owner");
        file.MarkAsQuarantined();
        file.MarkAsActive();
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var result = await _handler.Handle(new GetMediaUrlQuery(file.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Forbidden");
    }

    [Fact]
    public async Task Handle_NoUserId_ShouldReturnUnauthorized()
    {
        _currentUserMock.Setup(x => x.UserId).Returns(string.Empty);

        var result = await _handler.Handle(new GetMediaUrlQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Unauthorized");
    }
}
