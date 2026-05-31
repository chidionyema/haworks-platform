using FluentAssertions;
using Haworks.BuildingBlocks.Common;
using Haworks.Contracts.Merchant;
using Haworks.Merchant.Application.Common.Interfaces;
using Haworks.Merchant.Application.Merchants.Commands.ApproveMerchant;
using Haworks.Merchant.Domain.Aggregates;
using Haworks.Merchant.Domain.Enums;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Haworks.Merchant.Unit.Application;

public sealed class ApproveMerchantHandlerTests
{
    private readonly Mock<IPublishEndpoint> _mockPublishEndpoint;

    public ApproveMerchantHandlerTests()
    {
        _mockPublishEndpoint = new Mock<IPublishEndpoint>();
    }

    [Fact]
    public async Task Handle_nonexistent_merchant_returns_not_found()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var handler = new ApproveMerchantCommandHandler(context, _mockPublishEndpoint.Object);
        var command = new ApproveMerchantCommand(Guid.NewGuid(), "admin-123");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Merchant.NotFound");
    }

    [Fact]
    public async Task Handle_pending_merchant_approves_successfully()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var merchantId = Guid.NewGuid();
        var merchant = MerchantProfile.Create(Guid.NewGuid(), "Test Merchant", "test-merchant");
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);

        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();

        var handler = new ApproveMerchantCommandHandler(context, _mockPublishEndpoint.Object);
        var command = new ApproveMerchantCommand(merchantId, "admin-123");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var updated = await context.Merchants.FirstAsync(m => m.Id == merchantId);
        updated.Status.Should().Be(MerchantStatus.Active);
        updated.ApprovedBy.Should().Be("admin-123");
        updated.ApprovedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        _mockPublishEndpoint.Verify(x => x.Publish(
            It.Is<MerchantActivatedEvent>(e => e.MerchantId == merchantId),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_suspended_merchant_approves_successfully()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var merchantId = Guid.NewGuid();
        var merchant = MerchantProfile.Create(Guid.NewGuid(), "Test Merchant", "test-merchant");
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);

        // First activate then suspend
        merchant.Activate("admin-456");
        merchant.Suspend("admin-789", "Test suspension");

        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();

        var handler = new ApproveMerchantCommandHandler(context, _mockPublishEndpoint.Object);
        var command = new ApproveMerchantCommand(merchantId, "admin-123");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var updated = await context.Merchants.FirstAsync(m => m.Id == merchantId);
        updated.Status.Should().Be(MerchantStatus.Active);
        updated.ApprovedBy.Should().Be("admin-123");
    }

    [Fact]
    public async Task Handle_active_merchant_throws_invalid_operation()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var merchantId = Guid.NewGuid();
        var merchant = MerchantProfile.Create(Guid.NewGuid(), "Test Merchant", "test-merchant");
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);
        merchant.Activate("admin-456");

        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();

        var handler = new ApproveMerchantCommandHandler(context, _mockPublishEndpoint.Object);
        var command = new ApproveMerchantCommand(merchantId, "admin-123");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_rejected_merchant_throws_invalid_operation()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var merchantId = Guid.NewGuid();
        var merchant = MerchantProfile.Create(Guid.NewGuid(), "Test Merchant", "test-merchant");
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);
        merchant.Reject("admin-456", "Test rejection");

        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();

        var handler = new ApproveMerchantCommandHandler(context, _mockPublishEndpoint.Object);
        var command = new ApproveMerchantCommand(merchantId, "admin-123");

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