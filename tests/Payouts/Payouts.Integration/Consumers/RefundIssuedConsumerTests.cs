using FluentAssertions;
using Haworks.Contracts.Payments;
using Haworks.Payouts.Application.Ledger.Services;
using Haworks.Payouts.Domain.Enums;
using Haworks.Payouts.Infrastructure.Persistence;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haworks.Payouts.Integration.Consumers;

[Collection(nameof(PayoutsIntegrationTestDefinition))]
public class RefundIssuedConsumerTests : IAsyncLifetime
{
    private readonly PayoutsWebAppFactory _factory;
    private readonly IServiceScope _scope;
    private readonly PayoutsDbContext _db;
    private readonly ITestHarness _testHarness;
    private readonly ILedgerService _ledgerService;

    public RefundIssuedConsumerTests(PayoutsWebAppFactory factory)
    {
        _factory = factory;
        _scope = _factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<PayoutsDbContext>();
        _testHarness = _scope.ServiceProvider.GetRequiredService<ITestHarness>();
        _ledgerService = _scope.ServiceProvider.GetRequiredService<ILedgerService>();
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
    public async Task RefundIssuedEvent_Should_Debit_Seller_Account()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        // First credit the seller with a payment
        await _ledgerService.CreditSellerAsync(sellerId, 10000L, "USD", paymentId, "Test payment");
        await _db.SaveChangesAsync();

        var refundEvent = new RefundIssuedEvent
        {
            ProviderRefundId = Guid.NewGuid().ToString(),
            PaymentId = paymentId,
            OrderId = Guid.NewGuid(),
            AmountCents = 10000L,
            Currency = "USD",
            Provider = Haworks.Contracts.Payments.PaymentProvider.Stripe,
            IssuedAt = DateTime.UtcNow
        };

        // Act
        await _testHarness.Bus.Publish(refundEvent);

        // Assert
        Assert.True(await _testHarness.Consumed.Any<RefundIssuedEvent>());

        // Check that seller balance was debited
        var sellerBalance = await _ledgerService.GetBalanceAsync(sellerId, AccountType.SellerPending, "USD");
        sellerBalance.Should().Be(0L); // Should be back to zero after refund

        // Verify refund ledger entries were created
        var refundEntries = await _db.LedgerEntries
            .Where(e => e.ReferenceId.StartsWith("REFUND:"))
            .ToListAsync();

        refundEntries.Should().NotBeEmpty();
        refundEntries.Should().HaveCount(3); // Seller debit, platform credit, platform revenue debit
    }

    [Fact]
    public async Task RefundIssuedEvent_Should_Be_Idempotent_On_Duplicate_PaymentId()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        // First credit the seller
        await _ledgerService.CreditSellerAsync(sellerId, 5000L, "USD", paymentId, "Test payment");
        await _db.SaveChangesAsync();

        var refundEvent = new RefundIssuedEvent
        {
            ProviderRefundId = Guid.NewGuid().ToString(),
            PaymentId = paymentId,
            OrderId = Guid.NewGuid(),
            AmountCents = 5000L,
            Currency = "USD",
            Provider = Haworks.Contracts.Payments.PaymentProvider.Stripe,
            IssuedAt = DateTime.UtcNow
        };

        // Act - publish the same refund event twice
        await _testHarness.Bus.Publish(refundEvent);
        await _testHarness.Bus.Publish(refundEvent);

        // Assert
        Assert.True(await _testHarness.Consumed.Any<RefundIssuedEvent>());

        // Balance should be zero (not negative), proving idempotency
        var sellerBalance = await _ledgerService.GetBalanceAsync(sellerId, AccountType.SellerPending, "USD");
        sellerBalance.Should().Be(0L);

        // Should only have 3 refund entries (not 6)
        var refundEntries = await _db.LedgerEntries
            .Where(e => e.ReferenceId.StartsWith("REFUND:"))
            .ToListAsync();

        refundEntries.Should().HaveCount(3);
    }

    [Fact]
    public async Task RefundIssuedEvent_Should_Skip_When_No_Seller_Entry_Found()
    {
        // Arrange
        var refundEvent = new RefundIssuedEvent
        {
            ProviderRefundId = Guid.NewGuid().ToString(),
            PaymentId = Guid.NewGuid(), // Payment ID that doesn't exist
            OrderId = Guid.NewGuid(),
            AmountCents = 5000L,
            Currency = "USD",
            Provider = Haworks.Contracts.Payments.PaymentProvider.Stripe,
            IssuedAt = DateTime.UtcNow
        };

        // Act
        await _testHarness.Bus.Publish(refundEvent);

        // Assert
        Assert.True(await _testHarness.Consumed.Any<RefundIssuedEvent>());

        // Should not have created any refund entries
        var refundEntries = await _db.LedgerEntries
            .Where(e => e.ReferenceId.StartsWith("REFUND:"))
            .ToListAsync();

        refundEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task RefundIssuedEvent_Should_Handle_Partial_Refunds()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        // Credit seller with larger amount
        await _ledgerService.CreditSellerAsync(sellerId, 10000L, "USD", paymentId, "Test payment");
        await _db.SaveChangesAsync();

        var partialRefundEvent = new RefundIssuedEvent
        {
            ProviderRefundId = Guid.NewGuid().ToString(),
            PaymentId = paymentId,
            OrderId = Guid.NewGuid(),
            AmountCents = 3000L, // Partial refund
            Currency = "USD",
            Provider = Haworks.Contracts.Payments.PaymentProvider.Stripe,
            IssuedAt = DateTime.UtcNow
        };

        // Act
        await _testHarness.Bus.Publish(partialRefundEvent);

        // Assert
        Assert.True(await _testHarness.Consumed.Any<RefundIssuedEvent>());

        // Seller balance should be reduced by refund amount
        var sellerBalance = await _ledgerService.GetBalanceAsync(sellerId, AccountType.SellerPending, "USD");

        // Original: 9000 (10000 - 10% commission), after 3000 refund: 6000
        sellerBalance.Should().Be(6000L);
    }

    [Fact]
    public async Task RefundIssuedEvent_Should_Handle_Different_Currencies()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        // Credit seller with EUR payment
        await _ledgerService.CreditSellerAsync(sellerId, 8000L, "EUR", paymentId, "Test payment EUR");
        await _db.SaveChangesAsync();

        var refundEvent = new RefundIssuedEvent
        {
            ProviderRefundId = Guid.NewGuid().ToString(),
            PaymentId = paymentId,
            OrderId = Guid.NewGuid(),
            AmountCents = 8000L,
            Currency = "EUR",
            Provider = Haworks.Contracts.Payments.PaymentProvider.Stripe,
            IssuedAt = DateTime.UtcNow
        };

        // Act
        await _testHarness.Bus.Publish(refundEvent);

        // Assert
        Assert.True(await _testHarness.Consumed.Any<RefundIssuedEvent>());

        // Check EUR balance is back to zero
        var eurBalance = await _ledgerService.GetBalanceAsync(sellerId, AccountType.SellerPending, "EUR");
        eurBalance.Should().Be(0L);

        // USD balance should remain unaffected
        var usdBalance = await _ledgerService.GetBalanceAsync(sellerId, AccountType.SellerPending, "USD");
        usdBalance.Should().Be(0L); // No USD transactions
    }

    [Fact]
    public async Task RefundIssuedEvent_Should_Find_Seller_From_Pending_Or_Payable_Account()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        // Credit seller and then mature funds (moves to payable)
        await _ledgerService.CreditSellerAsync(sellerId, 5000L, "USD", paymentId, "Test payment");
        await _db.SaveChangesAsync();

        // Move funds from pending to payable (simulate fund maturity)
        var pendingAccount = await _db.LedgerAccounts
            .FirstAsync(a => a.OwnerId == sellerId && a.Type == AccountType.SellerPending);

        var payableAccount = Domain.Aggregates.LedgerAccount.Create(sellerId, AccountType.SellerPayable, "USD");
        payableAccount.UpdateBalance(pendingAccount.BalanceCents, EntryType.Credit);
        pendingAccount.UpdateBalance(pendingAccount.BalanceCents, EntryType.Debit);

        _db.LedgerAccounts.Add(payableAccount);
        await _db.SaveChangesAsync();

        var refundEvent = new RefundIssuedEvent
        {
            ProviderRefundId = Guid.NewGuid().ToString(),
            PaymentId = paymentId,
            OrderId = Guid.NewGuid(),
            AmountCents = 5000L,
            Currency = "USD",
            Provider = Haworks.Contracts.Payments.PaymentProvider.Stripe,
            IssuedAt = DateTime.UtcNow
        };

        // Act
        await _testHarness.Bus.Publish(refundEvent);

        // Assert
        Assert.True(await _testHarness.Consumed.Any<RefundIssuedEvent>());

        // Should find the seller even though funds moved to payable account
        var refundEntries = await _db.LedgerEntries
            .Where(e => e.ReferenceId.StartsWith("REFUND:"))
            .ToListAsync();

        refundEntries.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RefundIssuedEvent_Should_Handle_Multiple_Sellers_With_Same_PaymentId_Deterministically()
    {
        // This test ensures the consumer picks the first seller deterministically
        // when multiple sellers have entries for the same payment ID (edge case)

        // Arrange
        var seller1Id = Guid.NewGuid();
        var seller2Id = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        // Create entries for both sellers with same payment ID (unusual but possible)
        await _ledgerService.CreditSellerAsync(seller1Id, 3000L, "USD", paymentId, "Seller 1 payment");
        await _ledgerService.CreditSellerAsync(seller2Id, 3000L, "USD", paymentId, "Seller 2 payment");
        await _db.SaveChangesAsync();

        var refundEvent = new RefundIssuedEvent
        {
            ProviderRefundId = Guid.NewGuid().ToString(),
            PaymentId = paymentId,
            OrderId = Guid.NewGuid(),
            AmountCents = 3000L,
            Currency = "USD",
            Provider = Haworks.Contracts.Payments.PaymentProvider.Stripe,
            IssuedAt = DateTime.UtcNow
        };

        // Act
        await _testHarness.Bus.Publish(refundEvent);

        // Assert
        Assert.True(await _testHarness.Consumed.Any<RefundIssuedEvent>());

        // Should only process refund for one seller (the first one found)
        var refundEntries = await _db.LedgerEntries
            .Where(e => e.ReferenceId.StartsWith("REFUND:"))
            .ToListAsync();

        refundEntries.Should().HaveCount(3); // Only one set of refund entries, not two
    }
}