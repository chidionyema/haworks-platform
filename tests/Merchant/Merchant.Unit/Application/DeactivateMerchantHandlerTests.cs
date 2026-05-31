using FluentAssertions;
using Haworks.Contracts.Merchant;
using Haworks.Merchant.Application.Common.Interfaces;
using Haworks.Merchant.Application.Merchants.Commands.DeactivateMerchant;
using Haworks.Merchant.Domain.Aggregates;
using Haworks.Merchant.Domain.Enums;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Haworks.Merchant.Unit.Application;

public sealed class DeactivateMerchantHandlerTests
{
    private readonly Mock<IPublishEndpoint> _mockPublishEndpoint;

    public DeactivateMerchantHandlerTests()
    {
        _mockPublishEndpoint = new Mock<IPublishEndpoint>();
    }

    [Fact]
    public async Task Handle_nonexistent_merchant_returns_not_found()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var handler = new DeactivateMerchantCommandHandler(context, _mockPublishEndpoint.Object);
        var command = new DeactivateMerchantCommand(Guid.NewGuid(), Guid.NewGuid());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Merchant.NotFound");
    }

    [Fact]
    public async Task Handle_wrong_owner_returns_forbidden()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var ownerId = Guid.NewGuid();
        var wrongUserId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", "test-merchant");
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);
        merchant.Activate("admin-123");

        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();

        var handler = new DeactivateMerchantCommandHandler(context, _mockPublishEndpoint.Object);
        var command = new DeactivateMerchantCommand(merchantId, wrongUserId);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Merchant.Forbidden");
    }

    [Fact]
    public async Task Handle_valid_owner_active_merchant_deactivates_successfully()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var ownerId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", "test-merchant");
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);
        merchant.Activate("admin-123");

        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();

        var handler = new DeactivateMerchantCommandHandler(context, _mockPublishEndpoint.Object);
        var command = new DeactivateMerchantCommand(merchantId, ownerId);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var updated = await context.Merchants.FirstAsync(m => m.Id == merchantId);
        updated.Status.Should().Be(MerchantStatus.Deactivated);
        updated.DeactivatedBy.Should().Be(ownerId.ToString());
        updated.DeactivatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        _mockPublishEndpoint.Verify(x => x.Publish(
            It.Is<MerchantDeactivatedEvent>(e => e.MerchantId == merchantId),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_valid_owner_suspended_merchant_deactivates_successfully()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var ownerId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", "test-merchant");
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);
        merchant.Activate("admin-123");
        merchant.Suspend("admin-456", "Test suspension");

        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();

        var handler = new DeactivateMerchantCommandHandler(context, _mockPublishEndpoint.Object);
        var command = new DeactivateMerchantCommand(merchantId, ownerId);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var updated = await context.Merchants.FirstAsync(m => m.Id == merchantId);
        updated.Status.Should().Be(MerchantStatus.Deactivated);
        updated.DeactivatedBy.Should().Be(ownerId.ToString());
    }

    [Fact]
    public async Task Handle_pending_merchant_throws_invalid_operation()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var ownerId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", "test-merchant");
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);

        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();

        var handler = new DeactivateMerchantCommandHandler(context, _mockPublishEndpoint.Object);
        var command = new DeactivateMerchantCommand(merchantId, ownerId);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(command, CancellationToken.None));
    }

    private static TestMerchantDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<TestMerchantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestMerchantDbContext(options);
    }
}

internal sealed class TestMerchantDbContext : DbContext, IMerchantDbContext
{
    public TestMerchantDbContext(DbContextOptions<TestMerchantDbContext> options) : base(options) { }
    public DbSet<MerchantProfile> Merchants => Set<MerchantProfile>();
    public DbSet<OperatingHours> OperatingHours => Set<OperatingHours>();
}