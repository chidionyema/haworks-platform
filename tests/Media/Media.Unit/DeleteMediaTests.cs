using FluentAssertions;
using Haworks.BuildingBlocks.CurrentUser;
using Haworks.Contracts.Media;
using Haworks.Media.Api.Application;
using Haworks.Media.Api.Domain;
using Haworks.Media.Api.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Haworks.Media.Unit;

public class DeleteMediaTests
{
    private const string OwnerId = "delete-owner-1";
    private readonly MediaDbContext _context;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly Mock<IPublishEndpoint> _publisherMock;
    private readonly DeleteMediaHandler _handler;

    public DeleteMediaTests()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _context = new MediaDbContext(options);
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(x => x.UserId).Returns(OwnerId);
        _publisherMock = new Mock<IPublishEndpoint>();
        _handler = new DeleteMediaHandler(_context, _currentUserMock.Object, _publisherMock.Object, TimeProvider.System);
    }

    [Fact]
    public async Task Handle_OwnedFile_ShouldMarkDeletedAndPublishEvent()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", OwnerId);
        file.SetEntityLink(Guid.NewGuid(), "Product");
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var result = await _handler.Handle(new DeleteMediaCommand(file.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var updated = await _context.MediaFiles.FindAsync(file.Id);
        updated!.IsDeleted.Should().BeTrue();
        updated.Status.Should().Be(MediaStatus.Deleted);
        _publisherMock.Verify(x => x.Publish(
            It.Is<MediaDeletedEvent>(e => e.MediaId == file.Id && e.OwnerId == OwnerId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotFound_ShouldReturnFailure()
    {
        var result = await _handler.Handle(new DeleteMediaCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.NotFound");
    }

    [Fact]
    public async Task Handle_WrongOwner_ShouldReturnForbidden()
    {
        var file = MediaFile.Create("test.png", new string('a', 64), 1024, "image/png", "other-owner");
        _context.MediaFiles.Add(file);
        await _context.SaveChangesAsync();

        var result = await _handler.Handle(new DeleteMediaCommand(file.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Forbidden");
        var unchanged = await _context.MediaFiles.FindAsync(file.Id);
        unchanged!.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NoUserId_ShouldReturnUnauthorized()
    {
        _currentUserMock.Setup(x => x.UserId).Returns(string.Empty);

        var result = await _handler.Handle(new DeleteMediaCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Media.Unauthorized");
    }
}
