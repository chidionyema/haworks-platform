using FluentAssertions;
using Haworks.BuildingBlocks.Testing.Containers;
using Haworks.Merchant.Application.Merchants.Commands.RejectMerchant;
using Haworks.Merchant.Domain.Aggregates;
using Haworks.Merchant.Domain.Enums;
using Haworks.Merchant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haworks.Merchant.Unit.Application;

public sealed class RejectMerchantHandlerTests : IAsyncLifetime
{
    private string _connectionString = string.Empty;
    private MerchantDbContext? _context;

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
        var handler = new RejectMerchantCommandHandler(_context!);
        var command = new RejectMerchantCommand(Guid.NewGuid(), "admin-1", "Not found test");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Merchant.NotFound");
        result.Error.Message.Should().Be("Merchant not found.");
    }

    [Fact]
    public async Task Handle_valid_merchant_rejects_successfully()
    {
        // Arrange - Create merchant in Pending status
        var merchant = MerchantProfile.Create(Guid.NewGuid(), "Test Merchant", "test-merchant");
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var handler = new RejectMerchantCommandHandler(_context);
        var command = new RejectMerchantCommand(merchant.Id, "admin-1", "Insufficient documentation");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify merchant was rejected
        var updatedMerchant = await _context.Merchants.FirstAsync(m => m.Id == merchant.Id);
        updatedMerchant.Status.Should().Be(MerchantStatus.Rejected);
    }

    [Fact]
    public async Task Handle_active_merchant_can_be_rejected()
    {
        // Arrange - Create and activate a merchant
        var merchant = MerchantProfile.Create(Guid.NewGuid(), "Active Merchant", "active-merchant");
        merchant.Activate("admin-1");
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var handler = new RejectMerchantCommandHandler(_context);
        var command = new RejectMerchantCommand(merchant.Id, "admin-2", "Violates platform policies");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify merchant was rejected
        var updatedMerchant = await _context.Merchants.FirstAsync(m => m.Id == merchant.Id);
        updatedMerchant.Status.Should().Be(MerchantStatus.Rejected);
    }

    [Fact]
    public async Task Handle_suspended_merchant_can_be_rejected()
    {
        // Arrange - Create, activate, then suspend a merchant
        var merchant = MerchantProfile.Create(Guid.NewGuid(), "Suspended Merchant", "suspended-merchant");
        merchant.Activate("admin-1");
        merchant.Suspend("admin-1", "Previous violation");
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var handler = new RejectMerchantCommandHandler(_context);
        var command = new RejectMerchantCommand(merchant.Id, "admin-2", "Repeated violations");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify merchant was rejected
        var updatedMerchant = await _context.Merchants.FirstAsync(m => m.Id == merchant.Id);
        updatedMerchant.Status.Should().Be(MerchantStatus.Rejected);
    }

    [Fact]
    public async Task Handle_already_rejected_merchant_remains_rejected()
    {
        // Arrange - Create and reject a merchant
        var merchant = MerchantProfile.Create(Guid.NewGuid(), "Already Rejected", "already-rejected");
        merchant.Reject("admin-1", "Initial rejection");
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var handler = new RejectMerchantCommandHandler(_context);
        var command = new RejectMerchantCommand(merchant.Id, "admin-2", "Additional concerns");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify merchant is still rejected
        var updatedMerchant = await _context.Merchants.FirstAsync(m => m.Id == merchant.Id);
        updatedMerchant.Status.Should().Be(MerchantStatus.Rejected);
    }

    [Fact]
    public async Task Handle_inactive_merchant_can_be_rejected()
    {
        // Arrange - Create, activate, then deactivate a merchant
        var ownerId = Guid.NewGuid();
        var merchant = MerchantProfile.Create(ownerId, "Inactive Merchant", "inactive-merchant");
        merchant.Activate("admin-1");
        merchant.Deactivate(ownerId.ToString());
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var handler = new RejectMerchantCommandHandler(_context);
        var command = new RejectMerchantCommand(merchant.Id, "admin-2", "Review after deactivation");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify merchant was rejected
        var updatedMerchant = await _context.Merchants.FirstAsync(m => m.Id == merchant.Id);
        updatedMerchant.Status.Should().Be(MerchantStatus.Rejected);
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

        var handler = new RejectMerchantCommandHandler(_context);
        var command = new RejectMerchantCommand(merchant.Id, "admin-1", "Test rejection");

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.Handle(command, cts.Token));
    }

    [Fact]
    public async Task Handle_saves_changes_after_rejection()
    {
        // Arrange
        var merchant = MerchantProfile.Create(Guid.NewGuid(), "Test Merchant", "test-merchant");
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var handler = new RejectMerchantCommandHandler(_context);
        var command = new RejectMerchantCommand(merchant.Id, "admin-1", "Business verification failed");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify changes were persisted - reload context
        _context.ChangeTracker.Clear();
        var persistedMerchant = await _context.Merchants.FirstAsync(m => m.Id == merchant.Id);
        persistedMerchant.Status.Should().Be(MerchantStatus.Rejected);
    }
}