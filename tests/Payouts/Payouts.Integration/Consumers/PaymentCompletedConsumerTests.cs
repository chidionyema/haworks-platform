using FluentAssertions;
using Haworks.Contracts.Payments;
using Haworks.Payouts.Domain.Enums;
using Haworks.Payouts.Infrastructure.Persistence;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haworks.Payouts.Integration.Consumers;

[Collection(nameof(PayoutsIntegrationTestDefinition))]
public class PaymentCompletedConsumerTests : IAsyncLifetime
{
    private readonly PayoutsWebAppFactory _factory;
    private readonly IServiceScope _scope;
    private readonly PayoutsDbContext _db;
    private readonly ITestHarness _testHarness;

    public PaymentCompletedConsumerTests(PayoutsWebAppFactory factory)
    {
        _factory = factory;
        _scope = _factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<PayoutsDbContext>();
        _testHarness = _scope.ServiceProvider.GetRequiredService<ITestHarness>();
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureSchemaAsync();
        await _factory.ResetDatabaseAsync();
        await _testHarness.Start();
    }

    public async Task DisposeAsync()
    {
        await _testHarness.Stop();
        _scope.Dispose();
    }

    [Fact]
    public async Task PaymentCompletedEvent_Should_Credit_Seller_Account()
    {
        // Arrange
        var paymentEvent = new PaymentCompletedEvent
        {
            PaymentId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            SagaId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            AmountCents = 10000L,
            Currency = "USD",
            Provider = "Stripe"
        };

        // Act
        await _testHarness.Bus.Publish(paymentEvent);

        // Assert
        Assert.True(await _testHarness.Consumed.Any<PaymentCompletedEvent>());

        // Check ledger entries were created
        var entries = await _db.LedgerEntries
            .Where(e => e.ReferenceId == paymentEvent.PaymentId.ToString())
            .ToListAsync();

        entries.Should().NotBeEmpty();

        // Should have created 3 entries: seller credit, platform debit, platform revenue credit
        entries.Should().HaveCount(3);

        // Verify seller account was credited
        var sellerAccount = await _db.LedgerAccounts
            .Where(a => a.OwnerId == paymentEvent.SellerId && a.Type == AccountType.SellerPending)
            .FirstOrDefaultAsync();

        sellerAccount.Should().NotBeNull();
        sellerAccount!.BalanceCents.Should().Be(9000L); // 10000 - 10% commission = 9000

        // Verify platform accounts
        var platformId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var platformHolding = await _db.LedgerAccounts
            .Where(a => a.OwnerId == platformId && a.Type == AccountType.PlatformHolding)
            .FirstOrDefaultAsync();

        platformHolding.Should().NotBeNull();
        platformHolding!.BalanceCents.Should().BeGreaterOrEqualTo(10000L);

        var platformRevenue = await _db.LedgerAccounts
            .Where(a => a.OwnerId == platformId && a.Type == AccountType.PlatformRevenue)
            .FirstOrDefaultAsync();

        platformRevenue.Should().NotBeNull();
        platformRevenue!.BalanceCents.Should().BeGreaterOrEqualTo(1000L); // 10% commission
    }

    [Fact]
    public async Task PaymentCompletedEvent_Should_Be_Idempotent_On_Duplicate_PaymentId()
    {
        // Arrange
        var paymentEvent = new PaymentCompletedEvent
        {
            PaymentId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            AmountCents = 5000L,
            Currency = "USD",
            SagaId = Guid.NewGuid(),
            Provider = "Stripe"
        };

        // Act - publish the same event twice
        await _testHarness.Bus.Publish(paymentEvent);
        await _testHarness.Bus.Publish(paymentEvent);

        // Assert
        Assert.True(await _testHarness.Consumed.Any<PaymentCompletedEvent>());

        // Should only have 3 entries (not 6), proving idempotency
        var entries = await _db.LedgerEntries
            .Where(e => e.ReferenceId == paymentEvent.PaymentId.ToString())
            .ToListAsync();

        entries.Should().HaveCount(3);

        // Verify seller balance is not doubled
        var sellerAccount = await _db.LedgerAccounts
            .Where(a => a.OwnerId == paymentEvent.SellerId && a.Type == AccountType.SellerPending)
            .FirstOrDefaultAsync();

        sellerAccount.Should().NotBeNull();
        sellerAccount!.BalanceCents.Should().Be(4500L); // 5000 - 10% = 4500 (not 9000)
    }

    [Fact]
    public async Task PaymentCompletedEvent_Should_Skip_When_SellerId_Is_Empty()
    {
        // Arrange
        var paymentEvent = new PaymentCompletedEvent
        {
            PaymentId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            SellerId = Guid.Empty, // Empty seller ID
            AmountCents = 10000L,
            Currency = "USD",
            SagaId = Guid.NewGuid(),
            Provider = "Stripe"
        };

        // Act
        await _testHarness.Bus.Publish(paymentEvent);

        // Assert
        Assert.True(await _testHarness.Consumed.Any<PaymentCompletedEvent>());

        // Should not have created any ledger entries
        var entries = await _db.LedgerEntries
            .Where(e => e.ReferenceId == paymentEvent.PaymentId.ToString())
            .ToListAsync();

        entries.Should().BeEmpty();

        // Should not have created any accounts
        var accounts = await _db.LedgerAccounts.ToListAsync();
        accounts.Should().BeEmpty();
    }

    [Fact]
    public async Task PaymentCompletedEvent_Should_Handle_Different_Currencies()
    {
        // Arrange
        var paymentEvent = new PaymentCompletedEvent
        {
            PaymentId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            AmountCents = 7500L,
            Currency = "EUR",
            SagaId = Guid.NewGuid(),
            Provider = "Stripe"
        };

        // Act
        await _testHarness.Bus.Publish(paymentEvent);

        // Assert
        Assert.True(await _testHarness.Consumed.Any<PaymentCompletedEvent>());

        // Verify correct currency was used
        var sellerAccount = await _db.LedgerAccounts
            .Where(a => a.OwnerId == paymentEvent.SellerId && a.Type == AccountType.SellerPending)
            .FirstOrDefaultAsync();

        sellerAccount.Should().NotBeNull();
        sellerAccount!.Currency.Should().Be("EUR");
        sellerAccount.BalanceCents.Should().Be(6750L); // 7500 - 10% = 6750
    }

    [Fact]
    public async Task PaymentCompletedEvent_Should_Create_Seller_Profile_If_Not_Exists()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var paymentEvent = new PaymentCompletedEvent
        {
            PaymentId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            SellerId = sellerId,
            AmountCents = 3000L,
            Currency = "USD",
            SagaId = Guid.NewGuid(),
            Provider = "Stripe"
        };

        // Act
        await _testHarness.Bus.Publish(paymentEvent);

        // Assert
        Assert.True(await _testHarness.Consumed.Any<PaymentCompletedEvent>());

        // Should have automatically created seller profile with default commission
        var sellerProfile = await _db.SellerProfiles
            .Where(p => p.SellerId == sellerId)
            .FirstOrDefaultAsync();

        sellerProfile.Should().NotBeNull();
        sellerProfile!.CommissionPercentage.Should().Be(10m); // Default commission

        // Ledger should reflect commission calculation
        var sellerAccount = await _db.LedgerAccounts
            .Where(a => a.OwnerId == sellerId && a.Type == AccountType.SellerPending)
            .FirstOrDefaultAsync();

        sellerAccount.Should().NotBeNull();
        sellerAccount!.BalanceCents.Should().Be(2700L); // 3000 - 10% = 2700
    }
}