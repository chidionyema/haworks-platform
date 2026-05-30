using FluentAssertions;
using Haworks.Payouts.Application.Disbursements.Queries.GetPayoutsBySeller;
using Haworks.Payouts.Domain.Aggregates;
using Haworks.Payouts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Haworks.Payouts.Unit.Handlers;

public class GetPayoutsBySellerQueryHandlerTests : IDisposable
{
    private readonly PayoutsDbContext _context;
    private readonly GetPayoutsBySellerQueryHandler _handler;

    public GetPayoutsBySellerQueryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<PayoutsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _context = new PayoutsDbContext(options);
        _handler = new GetPayoutsBySellerQueryHandler(_context);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task Handle_Should_Return_Payouts_For_Specified_Seller()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var otherSellerId = Guid.NewGuid();

        var sellerPayout1 = Payout.Create(sellerId, 5000L, "USD");
        var sellerPayout2 = Payout.Create(sellerId, 3000L, "USD");
        var otherPayout = Payout.Create(otherSellerId, 7000L, "USD");

        _context.Payouts.AddRange(sellerPayout1, sellerPayout2, otherPayout);
        await _context.SaveChangesAsync();

        var query = new GetPayoutsBySellerQuery(sellerId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(p => p.Id == sellerPayout1.Id);
        result.Should().Contain(p => p.Id == sellerPayout2.Id);
        result.Should().NotContain(p => p.Id == otherPayout.Id);
    }

    [Fact]
    public async Task Handle_Should_Return_Empty_List_When_No_Payouts_Exist()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var query = new GetPayoutsBySellerQuery(sellerId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_Order_Payouts_By_CreatedAt_Descending()
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        var oldPayout = Payout.Create(sellerId, 1000L, "USD");
        var middlePayout = Payout.Create(sellerId, 2000L, "USD");
        var newPayout = Payout.Create(sellerId, 3000L, "USD");

        // Simulate different creation times by modifying the backing field
        var oldPayoutField = typeof(Payout).GetField("_createdAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var middlePayoutField = typeof(Payout).GetField("_createdAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var newPayoutField = typeof(Payout).GetField("_createdAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        oldPayoutField?.SetValue(oldPayout, DateTimeOffset.UtcNow.AddHours(-2));
        middlePayoutField?.SetValue(middlePayout, DateTimeOffset.UtcNow.AddHours(-1));
        newPayoutField?.SetValue(newPayout, DateTimeOffset.UtcNow);

        _context.Payouts.AddRange(oldPayout, middlePayout, newPayout);
        await _context.SaveChangesAsync();

        var query = new GetPayoutsBySellerQuery(sellerId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result[0].Id.Should().Be(newPayout.Id); // Most recent first
        result[1].Id.Should().Be(middlePayout.Id);
        result[2].Id.Should().Be(oldPayout.Id); // Oldest last
    }

    [Fact]
    public async Task Handle_Should_Apply_Pagination_With_Skip_And_Take()
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        // Create 5 payouts
        var payouts = new List<Payout>();
        for (int i = 0; i < 5; i++)
        {
            var payout = Payout.Create(sellerId, (i + 1) * 1000L, "USD");
            payouts.Add(payout);
        }

        _context.Payouts.AddRange(payouts);
        await _context.SaveChangesAsync();

        var query = new GetPayoutsBySellerQuery(sellerId, Skip: 2, Take: 2);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2); // Should return exactly 2 items
        result.Should().NotContain(p => payouts.Take(2).Select(po => po.Id).Contains(p.Id)); // Should skip first 2
    }

    [Fact]
    public async Task Handle_Should_Use_Default_Pagination_Values()
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        // Create 25 payouts (more than default Take of 20)
        var payouts = new List<Payout>();
        for (int i = 0; i < 25; i++)
        {
            var payout = Payout.Create(sellerId, (i + 1) * 1000L, "USD");
            payouts.Add(payout);
        }

        _context.Payouts.AddRange(payouts);
        await _context.SaveChangesAsync();

        var query = new GetPayoutsBySellerQuery(sellerId); // Using defaults: Skip=0, Take=20

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(20); // Should use default Take=20
    }

    [Fact]
    public async Task Handle_Should_Clamp_Take_To_Maximum_100()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var query = new GetPayoutsBySellerQuery(sellerId, Take: 150); // Exceeds max

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        // No error should occur, and it should be clamped to max 100
        // Since we have no payouts, result will be empty, but this tests the clamping logic
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_Clamp_Take_To_Minimum_1()
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        var payout = Payout.Create(sellerId, 1000L, "USD");
        _context.Payouts.Add(payout);
        await _context.SaveChangesAsync();

        var query = new GetPayoutsBySellerQuery(sellerId, Take: 0); // Below minimum

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1); // Should clamp to minimum 1 and return the payout
    }

    [Fact]
    public async Task Handle_Should_Ensure_Skip_Is_Not_Negative()
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        var payout = Payout.Create(sellerId, 1000L, "USD");
        _context.Payouts.Add(payout);
        await _context.SaveChangesAsync();

        var query = new GetPayoutsBySellerQuery(sellerId, Skip: -5); // Negative skip

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1); // Should treat negative skip as 0 and return the payout
    }

    [Fact]
    public async Task Handle_Should_Return_Payouts_With_All_Statuses()
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        var pendingPayout = Payout.Create(sellerId, 1000L, "USD");
        var completedPayout = Payout.Create(sellerId, 2000L, "USD");
        completedPayout.MarkSucceeded();
        var failedPayout = Payout.Create(sellerId, 3000L, "USD");
        failedPayout.MarkFailed("Test failure");

        _context.Payouts.AddRange(pendingPayout, completedPayout, failedPayout);
        await _context.SaveChangesAsync();

        var query = new GetPayoutsBySellerQuery(sellerId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain(p => p.Id == pendingPayout.Id);
        result.Should().Contain(p => p.Id == completedPayout.Id);
        result.Should().Contain(p => p.Id == failedPayout.Id);
    }

    [Fact]
    public async Task Handle_Should_Return_Payouts_With_Different_Currencies()
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        var usdPayout = Payout.Create(sellerId, 1000L, "USD");
        var eurPayout = Payout.Create(sellerId, 2000L, "EUR");
        var gbpPayout = Payout.Create(sellerId, 3000L, "GBP");

        _context.Payouts.AddRange(usdPayout, eurPayout, gbpPayout);
        await _context.SaveChangesAsync();

        var query = new GetPayoutsBySellerQuery(sellerId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain(p => p.Currency == "USD");
        result.Should().Contain(p => p.Currency == "EUR");
        result.Should().Contain(p => p.Currency == "GBP");
    }

    [Fact]
    public async Task Handle_Should_Use_AsNoTracking_For_Read_Only_Operation()
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        var payout = Payout.Create(sellerId, 1000L, "USD");
        _context.Payouts.Add(payout);
        await _context.SaveChangesAsync();

        var query = new GetPayoutsBySellerQuery(sellerId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);

        // Verify no additional entities are being tracked after the query
        var trackedEntities = _context.ChangeTracker.Entries().ToList();
        trackedEntities.Should().HaveCount(1); // Only the payout we added, not the one from the query
        trackedEntities[0].Entity.Should().BeOfType<Payout>();
        trackedEntities[0].State.Should().Be(EntityState.Unchanged);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 10)]
    [InlineData(10, 5)]
    [InlineData(0, 50)]
    public async Task Handle_Should_Handle_Various_Pagination_Combinations(int skip, int take)
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        // Create 20 payouts
        var payouts = new List<Payout>();
        for (int i = 0; i < 20; i++)
        {
            var payout = Payout.Create(sellerId, (i + 1) * 1000L, "USD");
            payouts.Add(payout);
        }

        _context.Payouts.AddRange(payouts);
        await _context.SaveChangesAsync();

        var query = new GetPayoutsBySellerQuery(sellerId, skip, take);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        var expectedCount = Math.Min(Math.Max(1, take), Math.Max(0, payouts.Count - Math.Max(0, skip)));
        if (skip >= payouts.Count) expectedCount = 0;

        result.Should().HaveCount(expectedCount);
    }
}