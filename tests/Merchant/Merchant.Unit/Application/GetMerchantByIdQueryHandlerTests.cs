using FluentAssertions;
using Haworks.Merchant.Application.Common.Interfaces;
using Haworks.Merchant.Application.Merchants.Queries;
using Haworks.Merchant.Domain.Aggregates;
using Haworks.Merchant.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Haworks.Merchant.Unit.Application;

public sealed class GetMerchantByIdQueryHandlerTests
{
    [Fact]
    public async Task Handle_nonexistent_merchant_returns_not_found()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var handler = new GetMerchantByIdQueryHandler(context);
        var query = new GetMerchantByIdQuery(Guid.NewGuid());

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Merchant.NotFound");
    }

    [Fact]
    public async Task Handle_existing_merchant_returns_success()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var merchantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", "test-merchant");
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);
        merchant.Activate("admin-123");

        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();

        var handler = new GetMerchantByIdQueryHandler(context);
        var query = new GetMerchantByIdQuery(merchantId);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Should().NotBeNull();
        dto.Id.Should().Be(merchantId);
        dto.OwnerId.Should().Be(ownerId);
        dto.Name.Should().Be("Test Merchant");
        dto.Slug.Should().Be("test-merchant");
        dto.Status.Should().Be(MerchantStatus.Active);
    }

    [Fact]
    public async Task Handle_merchant_with_operating_hours_includes_hours()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var merchantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", "test-merchant");
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);

        var operatingHours = OperatingHours.Create(
            merchantId,
            1, // Monday
            TimeSpan.FromHours(9),
            TimeSpan.FromHours(17),
            true);

        context.Merchants.Add(merchant);
        context.OperatingHours.Add(operatingHours);
        await context.SaveChangesAsync();

        var handler = new GetMerchantByIdQueryHandler(context);
        var query = new GetMerchantByIdQuery(merchantId);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.OperatingHours.Should().HaveCount(1);

        var hourDto = dto.OperatingHours.First();
        hourDto.Day.Should().Be(DayOfWeek.Monday);
        hourDto.Open.Should().Be(TimeSpan.FromHours(9));
        hourDto.Close.Should().Be(TimeSpan.FromHours(17));
        hourDto.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_merchant_with_complete_profile_returns_all_data()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var merchantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        var merchant = MerchantProfile.Create(ownerId, "Complete Merchant", "complete-merchant");
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);

        merchant.UpdateProfile(
            "Updated Name",
            "Test bio",
            "https://logo.url",
            "Test description",
            "test@example.com",
            "555-1234",
            "Restaurant",
            "https://website.com");

        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();

        var handler = new GetMerchantByIdQueryHandler(context);
        var query = new GetMerchantByIdQuery(merchantId);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Name.Should().Be("Updated Name");
        dto.Bio.Should().Be("Test bio");
        dto.LogoUrl.Should().Be("https://logo.url");
        dto.Description.Should().Be("Test description");
        dto.ContactEmail.Should().Be("test@example.com");
        dto.ContactPhone.Should().Be("555-1234");
        dto.Category.Should().Be("Restaurant");
        dto.Website.Should().Be("https://website.com");
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