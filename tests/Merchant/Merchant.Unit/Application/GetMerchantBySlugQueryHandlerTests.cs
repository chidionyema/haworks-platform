using FluentAssertions;
using Haworks.Merchant.Application.Common.Interfaces;
using Haworks.Merchant.Application.Merchants.Queries;
using Haworks.Merchant.Domain.Aggregates;
using Haworks.Merchant.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Haworks.Merchant.Unit.Application;

public sealed class GetMerchantBySlugQueryHandlerTests
{
    [Fact]
    public async Task Handle_nonexistent_slug_returns_not_found()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var handler = new GetMerchantBySlugQueryHandler(context);
        var query = new GetMerchantBySlugQuery("nonexistent-slug");

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Merchant.NotFound");
    }

    [Fact]
    public async Task Handle_existing_slug_returns_success()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var merchantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var slug = "unique-test-slug";

        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", slug);
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);

        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();

        var handler = new GetMerchantBySlugQueryHandler(context);
        var query = new GetMerchantBySlugQuery(slug);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Should().NotBeNull();
        dto.Id.Should().Be(merchantId);
        dto.OwnerId.Should().Be(ownerId);
        dto.Name.Should().Be("Test Merchant");
        dto.Slug.Should().Be(slug);
        dto.Status.Should().Be(MerchantStatus.Pending);
    }

    [Fact]
    public async Task Handle_slug_case_sensitive_search()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var ownerId = Guid.NewGuid();
        var slug = "Test-Slug";

        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", slug);
        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();

        var handler = new GetMerchantBySlugQueryHandler(context);
        var query = new GetMerchantBySlugQuery("test-slug"); // Different case

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert (assuming case-sensitive search)
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Merchant.NotFound");
    }

    [Fact]
    public async Task Handle_merchant_with_operating_hours_includes_hours()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var merchantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var slug = "merchant-with-hours";

        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", slug);
        merchant.GetType().GetProperty("Id")!.SetValue(merchant, merchantId);

        var operatingHours1 = OperatingHours.Create(
            merchantId,
            1, // Monday
            TimeSpan.FromHours(9),
            TimeSpan.FromHours(17),
            true);

        var operatingHours2 = OperatingHours.Create(
            merchantId,
            2, // Tuesday
            TimeSpan.FromHours(10),
            TimeSpan.FromHours(18),
            true);

        context.Merchants.Add(merchant);
        context.OperatingHours.AddRange(operatingHours1, operatingHours2);
        await context.SaveChangesAsync();

        var handler = new GetMerchantBySlugQueryHandler(context);
        var query = new GetMerchantBySlugQuery(slug);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.OperatingHours.Should().HaveCount(2);

        dto.OperatingHours.Should().Contain(h => h.Day == DayOfWeek.Monday && h.Open == TimeSpan.FromHours(9));
        dto.OperatingHours.Should().Contain(h => h.Day == DayOfWeek.Tuesday && h.Open == TimeSpan.FromHours(10));
    }

    [Fact]
    public async Task Handle_merchant_with_no_operating_hours_returns_empty_list()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var ownerId = Guid.NewGuid();
        var slug = "no-hours-merchant";

        var merchant = MerchantProfile.Create(ownerId, "Test Merchant", slug);
        context.Merchants.Add(merchant);
        await context.SaveChangesAsync();

        var handler = new GetMerchantBySlugQueryHandler(context);
        var query = new GetMerchantBySlugQuery(slug);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.OperatingHours.Should().BeEmpty();
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