using FluentAssertions;
using Haworks.Merchant.Application.Common.Interfaces;
using Haworks.Merchant.Application.Merchants.Queries;
using Haworks.Merchant.Domain.Aggregates;
using Haworks.Merchant.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Haworks.Merchant.Unit.Application;

public sealed class GetMerchantByOwnerQueryHandlerTests
{
    [Fact]
    public async Task Handle_nonexistent_owner_returns_not_found()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var handler = new GetMerchantByOwnerQueryHandler(context);
        var query = new GetMerchantByOwnerQuery(Guid.NewGuid());

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Merchant.NotFound");
    }

    [Fact]
    public async Task Handle_existing_owner_returns_success()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var merchantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        var merchant = MerchantProfile.Create(ownerId, "Owner's Merchant", "owners-merchant");
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);
        merchant.Activate("admin-123");

        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();

        var handler = new GetMerchantByOwnerQueryHandler(context);
        var query = new GetMerchantByOwnerQuery(ownerId);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Should().NotBeNull();
        dto.Id.Should().Be(merchantId);
        dto.OwnerId.Should().Be(ownerId);
        dto.Name.Should().Be("Owner's Merchant");
        dto.Slug.Should().Be("owners-merchant");
        dto.Status.Should().Be(MerchantStatus.Active);
    }

    [Fact]
    public async Task Handle_owner_with_multiple_merchants_returns_first()
    {
        // Arrange - Test assumes one merchant per owner based on business logic
        var context = CreateInMemoryContext();
        var ownerId = Guid.NewGuid();

        var merchant1 = MerchantProfile.Create(ownerId, "First Merchant", "first-merchant");
        var merchant2 = MerchantProfile.Create(ownerId, "Second Merchant", "second-merchant");

        context.Merchants.AddRange(merchant1, merchant2);
        await context.SaveChangesAsync();

        var handler = new GetMerchantByOwnerQueryHandler(context);
        var query = new GetMerchantByOwnerQuery(ownerId);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.OwnerId.Should().Be(ownerId);
        // Should return one of the merchants (implementation returns first found)
        new[] { "First Merchant", "Second Merchant" }.Should().Contain(dto.Name);
    }

    [Fact]
    public async Task Handle_owner_merchant_with_operating_hours_includes_hours()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var merchantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        var merchant = MerchantProfile.Create(ownerId, "Merchant with Hours", "merchant-hours");
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);

        var operatingHours = OperatingHours.Create(
            merchantId,
            3, // Wednesday
            TimeSpan.FromHours(8),
            TimeSpan.FromHours(20),
            true);

        context.Merchants.Add(merchant);
        context.OperatingHours.Add(operatingHours);
        await context.SaveChangesAsync();

        var handler = new GetMerchantByOwnerQueryHandler(context);
        var query = new GetMerchantByOwnerQuery(ownerId);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.OperatingHours.Should().HaveCount(1);

        var hourDto = dto.OperatingHours.First();
        hourDto.Day.Should().Be(DayOfWeek.Wednesday);
        hourDto.Open.Should().Be(TimeSpan.FromHours(8));
        hourDto.Close.Should().Be(TimeSpan.FromHours(20));
        hourDto.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_different_owner_ids_return_different_merchants()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var owner1Id = Guid.NewGuid();
        var owner2Id = Guid.NewGuid();

        var merchant1 = MerchantProfile.Create(owner1Id, "Owner 1 Merchant", "owner1-merchant");
        var merchant2 = MerchantProfile.Create(owner2Id, "Owner 2 Merchant", "owner2-merchant");

        context.Merchants.AddRange(merchant1, merchant2);
        await context.SaveChangesAsync();

        var handler = new GetMerchantByOwnerQueryHandler(context);

        // Act
        var result1 = await handler.Handle(new GetMerchantByOwnerQuery(owner1Id), CancellationToken.None);
        var result2 = await handler.Handle(new GetMerchantByOwnerQuery(owner2Id), CancellationToken.None);

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();

        result1.Value.Name.Should().Be("Owner 1 Merchant");
        result1.Value.OwnerId.Should().Be(owner1Id);

        result2.Value.Name.Should().Be("Owner 2 Merchant");
        result2.Value.OwnerId.Should().Be(owner2Id);

        result1.Value.Id.Should().NotBe(result2.Value.Id);
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