using FluentAssertions;
using Haworks.BuildingBlocks.Testing.Containers;
using Haworks.Contracts.Merchant;
using Haworks.Merchant.Application.Merchants.Commands.ApproveMerchant;
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

public sealed class ApproveMerchantHandlerTests : IAsyncLifetime
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
        var handler = new ApproveMerchantCommandHandler(_context!, _publishEndpointMock!.Object);
        var command = new ApproveMerchantCommand(Guid.NewGuid(), "admin-1");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Merchant.NotFound");
        result.Error.Message.Should().Be("Merchant not found.");

        // Verify no event was published
        _publishEndpointMock!.Verify(x => x.Publish(
            It.IsAny<MerchantActivatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_valid_merchant_approves_and_publishes_event()
    {
        // Arrange - Create a merchant in Pending status
        var merchant = MerchantProfile.Create(Guid.NewGuid(), "Test Merchant", "test-merchant");
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var handler = new ApproveMerchantCommandHandler(_context, _publishEndpointMock!.Object);
        var command = new ApproveMerchantCommand(merchant.Id, "admin-1");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify merchant was activated
        var updatedMerchant = await _context.Merchants.FirstAsync(m => m.Id == merchant.Id);
        updatedMerchant.Status.Should().Be(MerchantStatus.Active);

        // Verify event was published
        _publishEndpointMock!.Verify(x => x.Publish(
            It.Is<MerchantActivatedEvent>(e => e.MerchantId == merchant.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_already_active_merchant_remains_active()
    {
        // Arrange - Create and activate a merchant
        var merchant = MerchantProfile.Create(Guid.NewGuid(), "Test Merchant", "test-merchant");
        merchant.Activate("previous-admin");
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var handler = new ApproveMerchantCommandHandler(_context, _publishEndpointMock!.Object);
        var command = new ApproveMerchantCommand(merchant.Id, "admin-2");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify merchant is still active
        var updatedMerchant = await _context.Merchants.FirstAsync(m => m.Id == merchant.Id);
        updatedMerchant.Status.Should().Be(MerchantStatus.Active);

        // Event should still be published (idempotent behavior)
        _publishEndpointMock!.Verify(x => x.Publish(
            It.Is<MerchantActivatedEvent>(e => e.MerchantId == merchant.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_suspended_merchant_can_be_reactivated()
    {
        // Arrange - Create, activate, then suspend a merchant
        var merchant = MerchantProfile.Create(Guid.NewGuid(), "Test Merchant", "test-merchant");
        merchant.Activate("admin-1");
        merchant.Suspend("admin-1", "Test suspension");
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var handler = new ApproveMerchantCommandHandler(_context, _publishEndpointMock!.Object);
        var command = new ApproveMerchantCommand(merchant.Id, "admin-2");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify merchant was reactivated
        var updatedMerchant = await _context.Merchants.FirstAsync(m => m.Id == merchant.Id);
        updatedMerchant.Status.Should().Be(MerchantStatus.Active);

        // Verify event was published
        _publishEndpointMock!.Verify(x => x.Publish(
            It.Is<MerchantActivatedEvent>(e => e.MerchantId == merchant.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_rejected_merchant_can_be_approved()
    {
        // Arrange - Create and reject a merchant
        var merchant = MerchantProfile.Create(Guid.NewGuid(), "Test Merchant", "test-merchant");
        merchant.Reject("admin-1", "Initial rejection");
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var handler = new ApproveMerchantCommandHandler(_context, _publishEndpointMock!.Object);
        var command = new ApproveMerchantCommand(merchant.Id, "admin-2");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify merchant was activated
        var updatedMerchant = await _context.Merchants.FirstAsync(m => m.Id == merchant.Id);
        updatedMerchant.Status.Should().Be(MerchantStatus.Active);

        // Verify event was published
        _publishEndpointMock!.Verify(x => x.Publish(
            It.Is<MerchantActivatedEvent>(e => e.MerchantId == merchant.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_respects_cancellation_token()
    {
        // Arrange
        var merchant = MerchantProfile.Create(Guid.NewGuid(), "Test Merchant", "test-merchant");
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new ApproveMerchantCommandHandler(_context, _publishEndpointMock!.Object);
        var command = new ApproveMerchantCommand(merchant.Id, "admin-1");

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.Handle(command, cts.Token));
    }

    [Fact]
    public async Task Handle_saves_changes_after_activation_and_publish()
    {
        // Arrange
        var merchant = MerchantProfile.Create(Guid.NewGuid(), "Test Merchant", "test-merchant");
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var handler = new ApproveMerchantCommandHandler(_context, _publishEndpointMock!.Object);
        var command = new ApproveMerchantCommand(merchant.Id, "admin-1");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify changes were persisted - reload context
        _context.ChangeTracker.Clear();
        var persistedMerchant = await _context.Merchants.FirstAsync(m => m.Id == merchant.Id);
        persistedMerchant.Status.Should().Be(MerchantStatus.Active);
    }
}