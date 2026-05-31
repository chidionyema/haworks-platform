using FluentAssertions;
using Haworks.Merchant.Application.Common.Interfaces;
using Haworks.Merchant.Application.Merchants.Queries;
using Haworks.Merchant.Domain.Aggregates;
using Haworks.Merchant.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Haworks.Merchant.Unit.Application;

public sealed class ListMerchantsQueryHandlerTests
{
    [Fact]
    public async Task Handle_empty_database_returns_empty_list()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var handler = new ListMerchantsQueryHandler(context);
        var query = new ListMerchantsQuery(0, 10, null);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var pagedResult = result.Value;
        pagedResult.Items.Should().BeEmpty();
        pagedResult.Total.Should().Be(0);
        pagedResult.Skip.Should().Be(0);
        pagedResult.Take.Should().Be(10);
    }

    [Fact]
    public async Task Handle_no_filters_returns_all_non_deactivated_merchants()
    {
        // Arrange
        var context = CreateInMemoryContext();
        await SeedTestMerchants(context);

        var handler = new ListMerchantsQueryHandler(context);
        var query = new ListMerchantsQuery(0, 10, null);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var pagedResult = result.Value;
        pagedResult.Items.Should().HaveCount(3); // Pending, Active, Suspended (not Deactivated)
        pagedResult.Total.Should().Be(3);
        pagedResult.Items.Should().NotContain(m => m.Status == MerchantStatus.Deactivated);
    }

    [Fact]
    public async Task Handle_include_deactivated_returns_all_merchants()
    {
        // Arrange
        var context = CreateInMemoryContext();
        await SeedTestMerchants(context);

        var handler = new ListMerchantsQueryHandler(context);
        var query = new ListMerchantsQuery(0, 10, null, includeDeactivated: true);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var pagedResult = result.Value;
        pagedResult.Items.Should().HaveCount(4); // All statuses
        pagedResult.Total.Should().Be(4);
    }

    [Fact]
    public async Task Handle_filter_by_active_status_returns_only_active()
    {
        // Arrange
        var context = CreateInMemoryContext();
        await SeedTestMerchants(context);

        var handler = new ListMerchantsQueryHandler(context);
        var query = new ListMerchantsQuery(0, 10, MerchantStatus.Active);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var pagedResult = result.Value;
        pagedResult.Items.Should().HaveCount(1);
        pagedResult.Total.Should().Be(1);
        pagedResult.Items.Should().OnlyContain(m => m.Status == MerchantStatus.Active);
    }

    [Fact]
    public async Task Handle_filter_by_pending_status_returns_only_pending()
    {
        // Arrange
        var context = CreateInMemoryContext();
        await SeedTestMerchants(context);

        var handler = new ListMerchantsQueryHandler(context);
        var query = new ListMerchantsQuery(0, 10, MerchantStatus.Pending);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var pagedResult = result.Value;
        pagedResult.Items.Should().HaveCount(1);
        pagedResult.Total.Should().Be(1);
        pagedResult.Items.Should().OnlyContain(m => m.Status == MerchantStatus.Pending);
    }

    [Fact]
    public async Task Handle_pagination_skips_and_takes_correctly()
    {
        // Arrange
        var context = CreateInMemoryContext();
        await SeedTestMerchantsForPagination(context);

        var handler = new ListMerchantsQueryHandler(context);
        var query = new ListMerchantsQuery(2, 2, null); // Skip 2, take 2

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var pagedResult = result.Value;
        pagedResult.Items.Should().HaveCount(2);
        pagedResult.Total.Should().Be(5); // Total available
        pagedResult.Skip.Should().Be(2);
        pagedResult.Take.Should().Be(2);

        // Should be sorted by name, so items 3rd and 4th alphabetically
        pagedResult.Items.First().Name.Should().Be("Merchant C");
        pagedResult.Items.Last().Name.Should().Be("Merchant D");
    }

    [Fact]
    public async Task Handle_pagination_beyond_available_returns_empty()
    {
        // Arrange
        var context = CreateInMemoryContext();
        await SeedTestMerchantsForPagination(context);

        var handler = new ListMerchantsQueryHandler(context);
        var query = new ListMerchantsQuery(10, 5, null); // Skip beyond available

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var pagedResult = result.Value;
        pagedResult.Items.Should().BeEmpty();
        pagedResult.Total.Should().Be(5); // Total still correct
    }

    [Fact]
    public async Task Handle_merchants_with_operating_hours_includes_hours()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var merchantId = Guid.NewGuid();
        var merchant = MerchantProfile.Create(Guid.NewGuid(), "Merchant with Hours", "merchant-hours");
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

        var handler = new ListMerchantsQueryHandler(context);
        var query = new ListMerchantsQuery(0, 10, null);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value.Items.First();
        dto.OperatingHours.Should().HaveCount(1);
        dto.OperatingHours.First().Day.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public async Task Handle_ordered_by_name_alphabetically()
    {
        // Arrange
        var context = CreateInMemoryContext();
        await SeedTestMerchantsForPagination(context);

        var handler = new ListMerchantsQueryHandler(context);
        var query = new ListMerchantsQuery(0, 10, null);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var names = result.Value.Items.Select(m => m.Name).ToList();
        names.Should().BeInAscendingOrder();
        names.Should().Equal("Merchant A", "Merchant B", "Merchant C", "Merchant D", "Merchant E");
    }

    private static async Task SeedTestMerchants(TestMerchantDbContext context)
    {
        var pending = MerchantProfile.Create(Guid.NewGuid(), "Pending Merchant", "pending");

        var active = MerchantProfile.Create(Guid.NewGuid(), "Active Merchant", "active");
        active.Activate("admin-123");

        var suspended = MerchantProfile.Create(Guid.NewGuid(), "Suspended Merchant", "suspended");
        suspended.Activate("admin-123");
        suspended.Suspend("admin-456", "Test suspension");

        var deactivated = MerchantProfile.Create(Guid.NewGuid(), "Deactivated Merchant", "deactivated");
        deactivated.Activate("admin-123");
        deactivated.Deactivate("owner-789");

        context.Merchants.AddRange(pending, active, suspended, deactivated);
        await context.SaveChangesAsync();
    }

    private static async Task SeedTestMerchantsForPagination(TestMerchantDbContext context)
    {
        var merchants = new[]
        {
            MerchantProfile.Create(Guid.NewGuid(), "Merchant E", "merchant-e"),
            MerchantProfile.Create(Guid.NewGuid(), "Merchant A", "merchant-a"),
            MerchantProfile.Create(Guid.NewGuid(), "Merchant C", "merchant-c"),
            MerchantProfile.Create(Guid.NewGuid(), "Merchant B", "merchant-b"),
            MerchantProfile.Create(Guid.NewGuid(), "Merchant D", "merchant-d")
        };

        context.Merchants.AddRange(merchants);
        await context.SaveChangesAsync();
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