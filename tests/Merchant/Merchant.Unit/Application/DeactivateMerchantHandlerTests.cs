using FluentAssertions;
using Haworks.BuildingBlocks.Testing.Containers;
using Haworks.Contracts.Merchant;
using Haworks.Merchant.Application.Merchants.Commands.DeactivateMerchant;
using Haworks.Merchant.Domain.Aggregates;
using Haworks.Merchant.Domain.Enums;
using Haworks.Merchant.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Haworks.Merchant.Unit.Application;

public sealed class DeactivateMerchantHandlerTests : IAsyncLifetime
{
    private string _connectionString = string.Empty;
    private MerchantDbContext? _context;
    private Mock<IPublishEndpoint>? _publishEndpointMock;

    public async Task InitializeAsync()
    {
        _connectionString = await SharedTestPostgres.CreateDatabaseAsync("merchant");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:merchant"] = _connectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddDbContext<MerchantDbContext>(options =>
            options.UseNpgsql(_connectionString));

        var serviceProvider = services.BuildServiceProvider();
        _context = serviceProvider.GetRequiredService<MerchantDbContext>();

        await _context.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS merchant");
        await _context.Database.MigrateAsync();

        _publishEndpointMock = new Mock<IPublishEndpoint>();
    }

    public async Task DisposeAsync()
    {
        if (_context != null)
        {
            await _context.DisposeAsync();
        }
    }

    [Fact]
    public async Task Handle_merchant_not_found_returns_not_found_error()
    {
        // Arrange
        var handler = new DeactivateMerchantCommandHandler(_context!, _publishEndpointMock!.Object);
        var command = new DeactivateMerchantCommand(Guid.NewGuid(), Guid.NewGuid());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Merchant.NotFound");
        result.Error.Message.Should().Be("Merchant not found.");

        // Verify no event was published
        _publishEndpointMock!.Verify(x => x.Publish(
            It.IsAny<MerchantDeactivatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_wrong_owner_returns_forbidden_error()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", "test-merchant");
        merchant.Activate("admin-1");
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var differentUserId = Guid.NewGuid();
        var handler = new DeactivateMerchantCommandHandler(_context, _publishEndpointMock!.Object);
        var command = new DeactivateMerchantCommand(merchant.Id, differentUserId);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Merchant.Forbidden");
        result.Error.Message.Should().Be("You are not authorized to deactivate this merchant.");

        // Verify no event was published
        _publishEndpointMock!.Verify(x => x.Publish(
            It.IsAny<MerchantDeactivatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_valid_owner_deactivates_merchant_and_publishes_event()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", "test-merchant");
        merchant.Activate("admin-1");
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var handler = new DeactivateMerchantCommandHandler(_context, _publishEndpointMock!.Object);
        var command = new DeactivateMerchantCommand(merchant.Id, ownerId);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify merchant was deactivated
        var updatedMerchant = await _context.Merchants.FirstAsync(m => m.Id == merchant.Id);
        updatedMerchant.Status.Should().Be(MerchantStatus.Inactive);

        // Verify event was published
        _publishEndpointMock!.Verify(x => x.Publish(
            It.Is<MerchantDeactivatedEvent>(e => e.MerchantId == merchant.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_already_inactive_merchant_remains_inactive()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", "test-merchant");
        merchant.Activate("admin-1");
        merchant.Deactivate(ownerId.ToString());
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var handler = new DeactivateMerchantCommandHandler(_context, _publishEndpointMock!.Object);
        var command = new DeactivateMerchantCommand(merchant.Id, ownerId);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify merchant is still inactive
        var updatedMerchant = await _context.Merchants.FirstAsync(m => m.Id == merchant.Id);
        updatedMerchant.Status.Should().Be(MerchantStatus.Inactive);

        // Event should still be published (idempotent behavior)
        _publishEndpointMock!.Verify(x => x.Publish(
            It.Is<MerchantDeactivatedEvent>(e => e.MerchantId == merchant.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_pending_merchant_can_be_deactivated()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", "test-merchant");
        // Merchant remains in Pending status
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var handler = new DeactivateMerchantCommandHandler(_context, _publishEndpointMock!.Object);
        var command = new DeactivateMerchantCommand(merchant.Id, ownerId);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify merchant was deactivated
        var updatedMerchant = await _context.Merchants.FirstAsync(m => m.Id == merchant.Id);
        updatedMerchant.Status.Should().Be(MerchantStatus.Inactive);

        // Verify event was published
        _publishEndpointMock!.Verify(x => x.Publish(
            It.Is<MerchantDeactivatedEvent>(e => e.MerchantId == merchant.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_respects_cancellation_token()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", "test-merchant");
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new DeactivateMerchantCommandHandler(_context, _publishEndpointMock!.Object);
        var command = new DeactivateMerchantCommand(merchant.Id, ownerId);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.Handle(command, cts.Token));
    }

    [Fact]
    public async Task Handle_saves_changes_after_deactivation_and_publish()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", "test-merchant");
        merchant.Activate("admin-1");
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var handler = new DeactivateMerchantCommandHandler(_context, _publishEndpointMock!.Object);
        var command = new DeactivateMerchantCommand(merchant.Id, ownerId);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify changes were persisted - reload context
        _context.ChangeTracker.Clear();
        var persistedMerchant = await _context.Merchants.FirstAsync(m => m.Id == merchant.Id);
        persistedMerchant.Status.Should().Be(MerchantStatus.Inactive);
    }
}