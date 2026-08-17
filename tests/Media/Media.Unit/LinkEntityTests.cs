using FluentAssertions;
using Haworks.BuildingBlocks.CurrentUser;
using Haworks.Media.Api.Application;
using Haworks.Media.Api.Domain;
using Haworks.Media.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Haworks.Media.Unit;

public class LinkEntityTests
{
    private const string OwnerId = "link-owner-1";
    private readonly MediaDbContext _context;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly LinkEntityHandler _handler;

    public LinkEntityTests()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new MediaDbContext(options);
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(x => x.UserId).Returns(OwnerId);
        _handler = new LinkEntityHandler(_context, _currentUserMock.Object);
    }

    [Fact]
    public async Task Handle_OwnedFile_ShouldSetEntityLink()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();
        var entityId = Guid.NewGuid();

        var result = await _handler.Handle(new LinkEntityCommand(file.Id, entityId, "Product"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var updated = await _context.MediaFiles.FindAsync(file.Id);
        updated!.EntityId.Should().Be(entityId);
        updated.EntityType.Should().Be("Product");
    }

    [Fact]
    public async Task Handle_NotFound_ShouldReturnFailure()
    {
        var result = await _handler.Handle(new LinkEntityCommand(Guid.NewGuid(), Guid.NewGuid(), "Product"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.NotFound");
    }

    [Fact]
    public async Task Handle_WrongOwner_ShouldReturnForbidden()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "other-owner");
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var result = await _handler.Handle(new LinkEntityCommand(file.Id, Guid.NewGuid(), "Product"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Forbidden");
    }

    [Fact]
    public async Task Handle_NoUserId_ShouldReturnUnauthorized()
    {
        _currentUserMock.Setup(x => x.UserId).Returns(string.Empty);

        var result = await _handler.Handle(new LinkEntityCommand(Guid.NewGuid(), Guid.NewGuid(), "Product"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Unauthorized");
    }
}
