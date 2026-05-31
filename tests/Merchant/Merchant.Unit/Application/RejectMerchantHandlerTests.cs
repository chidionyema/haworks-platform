using FluentAssertions;
using Haworks.Merchant.Application.Common.Interfaces;
using Haworks.Merchant.Application.Merchants.Commands.RejectMerchant;
using Haworks.Merchant.Domain.Aggregates;
using Haworks.Merchant.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Haworks.Merchant.Unit.Application;

public sealed class RejectMerchantHandlerTests
{
    [Fact]
    public async Task Handle_nonexistent_merchant_returns_not_found()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var handler = new RejectMerchantCommandHandler(context);
        var command = new RejectMerchantCommand(Guid.NewGuid(), "admin-123", "Test reason");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Merchant.NotFound");
    }

    [Fact]
    public async Task Handle_pending_merchant_rejects_successfully()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var merchantId = Guid.NewGuid();
        var merchant = MerchantProfile.Create(Guid.NewGuid(), "Test Merchant", "test-merchant");
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);

        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();

        var handler = new RejectMerchantCommandHandler(context);
        var command = new RejectMerchantCommand(merchantId, "admin-123", "Invalid documents");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var updated = await context.Merchants.FirstAsync(m => m.Id == merchantId);
        updated.Status.Should().Be(MerchantStatus.Rejected);
        updated.RejectedBy.Should().Be("admin-123");
        updated.RejectionReason.Should().Be("Invalid documents");
        updated.RejectedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
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

        var handler = new RejectMerchantCommandHandler(context);
        var command = new RejectMerchantCommand(merchantId, "admin-123", "Test reason");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_already_rejected_merchant_throws_invalid_operation()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var merchantId = Guid.NewGuid();
        var merchant = MerchantProfile.Create(Guid.NewGuid(), "Test Merchant", "test-merchant");
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);
        merchant.Reject("admin-456", "Previous rejection");

        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();

        var handler = new RejectMerchantCommandHandler(context);
        var command = new RejectMerchantCommand(merchantId, "admin-123", "New reason");

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