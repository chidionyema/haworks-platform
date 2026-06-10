using FluentAssertions;
using Haworks.BuildingBlocks.Testing.Containers;
using Haworks.Merchant.Application.Common.Interfaces;
using Haworks.Merchant.Application.Merchants.Commands.UpdateMerchant;
using Haworks.Merchant.Domain.Aggregates;
using Haworks.Merchant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Haworks.Merchant.Unit.Application;

public sealed class UpdateMerchantHandlerTests : IAsyncLifetime
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
    public async Task Handle_nonexistent_merchant_returns_not_found()
    {
        var handler = new UpdateMerchantCommandHandler(_context!);

        var command = new UpdateMerchantCommand(Guid.NewGuid(), Guid.NewGuid(), "Name", null, null, null, null, null, null, null);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Merchant.NotFound");
    }

    [Fact]
    public async Task Handle_wrong_owner_returns_forbidden()
    {
        var ownerId = Guid.NewGuid();
        var merchant = MerchantProfile.Create(ownerId, "Test", "test");
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var handler = new UpdateMerchantCommandHandler(_context);
        var differentUserId = Guid.NewGuid();
        var command = new UpdateMerchantCommand(merchant.Id, differentUserId, "New Name", null, null, null, null, null, null, null);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Merchant.Forbidden");
    }

    [Fact]
    public async Task Handle_valid_owner_updates_successfully()
    {
        var ownerId = Guid.NewGuid();
        var merchant = MerchantProfile.Create(ownerId, "Test", "test");
        _context!.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var handler = new UpdateMerchantCommandHandler(_context);
        var command = new UpdateMerchantCommand(merchant.Id, ownerId, "Updated", "New Bio", null, null, null, null, null, null);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var updated = await _context.Merchants.FirstAsync(m => m.Id == merchant.Id);
        updated.Name.Should().Be("Updated");
        updated.Bio.Should().Be("New Bio");
    }
}
