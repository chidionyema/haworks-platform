using FluentAssertions;
using Haworks.BuildingBlocks.CurrentUser;
using Haworks.Media.Api.Application;
using Haworks.Media.Api.Domain;
using Haworks.Media.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Haworks.Media.Unit;

public class ListMediaTests
{
    private const string OwnerId = "list-owner-1";
    private readonly MediaDbContext _context;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly ListMediaHandler _handler;

    public ListMediaTests()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new MediaDbContext(options);
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(x => x.UserId).Returns(OwnerId);
        _handler = new ListMediaHandler(_context, _currentUserMock.Object);
    }

    private async Task SeedAsync()
    {
        var active = MediaFile.Create("a.png", new string('a', 64), 1024, "image/png", OwnerId);
        active.MarkAsQuarantined();
        active.MarkAsActive();

        var rejected = MediaFile.Create("b.png", new string('b', 64), 1024, "image/png", OwnerId);
        rejected.MarkAsQuarantined();
        rejected.MarkAsRejected();

        var pendingVideo = MediaFile.Create("c.mp4", new string('c', 64), 1024, "video/mp4", OwnerId);

        var otherOwnerFile = MediaFile.Create("d.png", new string('d', 64), 1024, "image/png", "someone-else");

        _context.MediaFiles.AddRange(active, rejected, pendingVideo, otherOwnerFile);
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_NoFilters_ShouldReturnOnlyCallingOwnersFiles()
    {
        await SeedAsync();

        var result = await _handler.Handle(new ListMediaQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(3);
        result.Value.Items.Should().OnlyContain(i => i.FileName != "d.png");
    }

    [Fact]
    public async Task Handle_StatusFilter_ShouldReturnOnlyMatchingStatus()
    {
        await SeedAsync();

        var result = await _handler.Handle(new ListMediaQuery(Status: "Active"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].FileName.Should().Be("a.png");
    }

    [Fact]
    public async Task Handle_MimeTypePrefixFilter_ShouldReturnOnlyMatchingMimeType()
    {
        await SeedAsync();

        var result = await _handler.Handle(new ListMediaQuery(MimeTypePrefix: "video/"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].FileName.Should().Be("c.mp4");
    }

    [Fact]
    public async Task Handle_Pagination_ShouldRespectPageSize()
    {
        await SeedAsync();

        var result = await _handler.Handle(new ListMediaQuery(Page: 1, PageSize: 2), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task Handle_NoUserId_ShouldReturnUnauthorized()
    {
        _currentUserMock.Setup(x => x.UserId).Returns(string.Empty);

        var result = await _handler.Handle(new ListMediaQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Unauthorized");
    }
}
