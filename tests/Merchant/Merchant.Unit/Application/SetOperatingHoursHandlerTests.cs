using FluentAssertions;
using Haworks.Merchant.Application.Common.Interfaces;
using Haworks.Merchant.Application.Merchants.Commands.SetOperatingHours;
using Haworks.Merchant.Application.Merchants.DTOs;
using Haworks.Merchant.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Haworks.Merchant.Unit.Application;

public sealed class SetOperatingHoursHandlerTests
{
    [Fact]
    public async Task Handle_nonexistent_merchant_returns_not_found()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var handler = new SetOperatingHoursCommandHandler(context);
        var hours = new List<OperatingHourDto>
        {
            new(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(17), true)
        };
        var command = new SetOperatingHoursCommand(Guid.NewGuid(), Guid.NewGuid(), hours);

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

        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();

        var hours = new List<OperatingHourDto>
        {
            new(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(17), true)
        };

        var handler = new SetOperatingHoursCommandHandler(context);
        var command = new SetOperatingHoursCommand(merchantId, wrongUserId, hours);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Merchant.Forbidden");
    }

    [Fact]
    public async Task Handle_valid_owner_sets_operating_hours_successfully()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var ownerId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", "test-merchant");
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);

        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();

        var hours = new List<OperatingHourDto>
        {
            new(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(17), true),
            new(DayOfWeek.Tuesday, TimeSpan.FromHours(10), TimeSpan.FromHours(18), true),
            new(DayOfWeek.Sunday, TimeSpan.Zero, TimeSpan.Zero, false)
        };

        var handler = new SetOperatingHoursCommandHandler(context);
        var command = new SetOperatingHoursCommand(merchantId, ownerId, hours);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var savedHours = await context.OperatingHours
            .Where(h => h.MerchantId == merchantId)
            .ToListAsync();

        savedHours.Should().HaveCount(3);
        savedHours.Should().Contain(h => h.DayOfWeek == 1 && h.OpenTime == TimeSpan.FromHours(9) && h.CloseTime == TimeSpan.FromHours(17) && h.IsOpen);
        savedHours.Should().Contain(h => h.DayOfWeek == 2 && h.OpenTime == TimeSpan.FromHours(10) && h.CloseTime == TimeSpan.FromHours(18) && h.IsOpen);
        savedHours.Should().Contain(h => h.DayOfWeek == 0 && !h.IsOpen);
    }

    [Fact]
    public async Task Handle_replaces_existing_operating_hours()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var ownerId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", "test-merchant");
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);

        // Add existing hours
        var existingHours = OperatingHours.Create(merchantId, 1, TimeSpan.FromHours(8), TimeSpan.FromHours(16), true);
        context.Merchants.Add(merchant);
        context.OperatingHours.Add(existingHours);
        await context.SaveChangesAsync();

        var newHours = new List<OperatingHourDto>
        {
            new(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(17), true),
            new(DayOfWeek.Friday, TimeSpan.FromHours(9), TimeSpan.FromHours(15), true)
        };

        var handler = new SetOperatingHoursCommandHandler(context);
        var command = new SetOperatingHoursCommand(merchantId, ownerId, newHours);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var savedHours = await context.OperatingHours
            .Where(h => h.MerchantId == merchantId)
            .ToListAsync();

        savedHours.Should().HaveCount(2);
        savedHours.Should().NotContain(h => h.OpenTime == TimeSpan.FromHours(8)); // Old hours removed
        savedHours.Should().Contain(h => h.DayOfWeek == 1 && h.OpenTime == TimeSpan.FromHours(9) && h.CloseTime == TimeSpan.FromHours(17));
        savedHours.Should().Contain(h => h.DayOfWeek == 5 && h.OpenTime == TimeSpan.FromHours(9) && h.CloseTime == TimeSpan.FromHours(15));
    }

    [Fact]
    public async Task Handle_empty_hours_list_removes_all_existing()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var ownerId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", "test-merchant");
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);

        // Add existing hours
        var existingHours = OperatingHours.Create(merchantId, 1, TimeSpan.FromHours(8), TimeSpan.FromHours(16), true);
        context.Merchants.Add(merchant);
        context.OperatingHours.Add(existingHours);
        await context.SaveChangesAsync();

        var handler = new SetOperatingHoursCommandHandler(context);
        var command = new SetOperatingHoursCommand(merchantId, ownerId, new List<OperatingHourDto>());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var savedHours = await context.OperatingHours
            .Where(h => h.MerchantId == merchantId)
            .ToListAsync();

        savedHours.Should().BeEmpty();
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