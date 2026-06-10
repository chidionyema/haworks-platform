using FluentAssertions;
using Haworks.BuildingBlocks.Testing.Containers;
using Haworks.Contracts.Merchant;
using Haworks.Merchant.Application.Merchants.Commands.CreateMerchant;
using Haworks.Merchant.Domain.Aggregates;
using Haworks.Merchant.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using Xunit;

namespace Haworks.Merchant.Unit.Application;

public sealed class CreateMerchantHandlerTests : IAsyncLifetime
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
    public async Task Handle_duplicate_owner_throws_exception()
    {
        // Arrange - Create existing merchant with owner
        var ownerId = Guid.NewGuid();
        var existingMerchant = MerchantProfile.Create(ownerId, "Existing", "existing-slug");
        _context!.Merchants.Add(existingMerchant);
        await _context.SaveChangesAsync();

        var handler = new CreateMerchantCommandHandler(_context, _publishEndpointMock!.Object);
        var command = new CreateMerchantCommand(ownerId, "New Merchant", "new-slug");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(command, CancellationToken.None));
        exception.Message.Should().Be("Owner already has a merchant.");
    }

    [Fact]
    public async Task Handle_duplicate_slug_throws_exception()
    {
        // Arrange - Create existing merchant with slug
        var existingMerchant = MerchantProfile.Create(Guid.NewGuid(), "Existing", "duplicate-slug");
        _context!.Merchants.Add(existingMerchant);
        await _context.SaveChangesAsync();

        var handler = new CreateMerchantCommandHandler(_context, _publishEndpointMock!.Object);
        var command = new CreateMerchantCommand(Guid.NewGuid(), "New Merchant", "duplicate-slug");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(command, CancellationToken.None));
        exception.Message.Should().Be("Slug is already in use");
    }

    [Fact]
    public async Task Handle_valid_request_creates_merchant_and_publishes_event()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var handler = new CreateMerchantCommandHandler(_context!, _publishEndpointMock!.Object);
        var command = new CreateMerchantCommand(ownerId, "Test Merchant", "test-merchant");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeEmpty();

        var createdMerchant = await _context!.Merchants.FirstOrDefaultAsync(m => m.Id == result);
        createdMerchant.Should().NotBeNull();
        createdMerchant!.OwnerId.Should().Be(ownerId);
        createdMerchant.Name.Should().Be("Test Merchant");
        createdMerchant.Slug.Should().Be("test-merchant");

        // Verify event was published
        _publishEndpointMock!.Verify(x => x.Publish(
            It.Is<MerchantCreatedEvent>(e =>
                e.MerchantId == result &&
                e.OwnerId == ownerId &&
                e.Name == "Test Merchant" &&
                e.Slug == "test-merchant"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_concurrent_insert_owner_duplicate_throws_wrapped_exception()
    {
        // Arrange - Simulate concurrent insert by manually triggering constraint violation
        var ownerId = Guid.NewGuid();
        var handler = new CreateMerchantCommandHandler(_context!, _publishEndpointMock!.Object);

        // Create merchant directly in DB to simulate race condition
        await _context!.Database.ExecuteSqlRawAsync(
            "INSERT INTO merchant.\"MerchantProfiles\" (\"Id\", \"OwnerId\", \"Name\", \"Slug\", \"Status\", \"StatusHistory\", \"CreatedAt\", \"UpdatedAt\") VALUES (@id, @ownerId, @name, @slug, 0, '[]', @now, @now)",
            new NpgsqlParameter("@id", Guid.NewGuid()),
            new NpgsqlParameter("@ownerId", ownerId),
            new NpgsqlParameter("@name", "Race Merchant"),
            new NpgsqlParameter("@slug", "race-merchant"),
            new NpgsqlParameter("@now", DateTimeOffset.UtcNow));

        var command = new CreateMerchantCommand(ownerId, "Test Merchant", "test-merchant");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(command, CancellationToken.None));
        exception.Message.Should().Be("Owner or slug already exists (concurrent insert detected).");
        exception.InnerException.Should().NotBeNull();
        exception.InnerException.Should().BeOfType<DbUpdateException>();
    }

    [Fact]
    public async Task Handle_concurrent_insert_slug_duplicate_throws_wrapped_exception()
    {
        // Arrange - Simulate concurrent insert by manually triggering constraint violation
        var slug = "race-slug";
        var handler = new CreateMerchantCommandHandler(_context!, _publishEndpointMock!.Object);

        // Create merchant directly in DB to simulate race condition
        await _context!.Database.ExecuteSqlRawAsync(
            "INSERT INTO merchant.\"MerchantProfiles\" (\"Id\", \"OwnerId\", \"Name\", \"Slug\", \"Status\", \"StatusHistory\", \"CreatedAt\", \"UpdatedAt\") VALUES (@id, @ownerId, @name, @slug, 0, '[]', @now, @now)",
            new NpgsqlParameter("@id", Guid.NewGuid()),
            new NpgsqlParameter("@ownerId", Guid.NewGuid()),
            new NpgsqlParameter("@name", "Race Merchant"),
            new NpgsqlParameter("@slug", slug),
            new NpgsqlParameter("@now", DateTimeOffset.UtcNow));

        var command = new CreateMerchantCommand(Guid.NewGuid(), "Test Merchant", slug);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(command, CancellationToken.None));
        exception.Message.Should().Be("Owner or slug already exists (concurrent insert detected).");
    }

    [Fact]
    public async Task Handle_respects_cancellation_token()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new CreateMerchantCommandHandler(_context!, _publishEndpointMock!.Object);
        var command = new CreateMerchantCommand(Guid.NewGuid(), "Test Merchant", "test-merchant");

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.Handle(command, cts.Token));
    }
}