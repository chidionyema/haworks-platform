using FluentAssertions;
using Haworks.Contracts.Payments;
using Haworks.Payouts.Application.Ledger.Services;
using Haworks.Payouts.Domain.Aggregates;
using Haworks.Payouts.Domain.Enums;
using Haworks.Payouts.Infrastructure.Persistence;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haworks.Payouts.Integration.Consumers;

[Collection(nameof(PayoutsIntegrationTestDefinition))]
public class ConsumerIntegrationTests : IAsyncLifetime
{
    private readonly PayoutsWebAppFactory _factory;
    private readonly IServiceScope _scope;
    private readonly ITestHarness _harness;
    private readonly PayoutsDbContext _db;
    private readonly ILedgerService _ledgerService;

    public ConsumerIntegrationTests(PayoutsWebAppFactory factory)
    {
        _factory = factory;
        _scope = _factory.Services.CreateScope();
        _harness = _scope.ServiceProvider.GetRequiredService<ITestHarness>();
        _db = _scope.ServiceProvider.GetRequiredService<PayoutsDbContext>();
        _ledgerService = _scope.ServiceProvider.GetRequiredService<ILedgerService>();
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureSchemaAsync();
        await _factory.ResetDatabaseAsync();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        _scope.Dispose();
    }

    [Fact]
    public async Task PaymentCompletedConsumer_Should_Credit_Seller_Account()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var amountCents = 10000L;
        var currency = "USD";

        // Create seller profile for commission calculation
        var profile = SellerProfile.Create(sellerId);
        profile.CommissionPercentage = 10m;
        _db.SellerProfiles.Add(profile);
        await _db.SaveChangesAsync();

        var paymentEvent = new PaymentCompletedEvent
        {
            PaymentId = paymentId,
            OrderId = orderId,
            SellerId = sellerId,
            AmountCents = amountCents,
            Currency = currency
        };

        // Act
        await _harness.Bus.Publish(paymentEvent);

        // Assert
        await _harness.Consumed.Any<PaymentCompletedEvent>();

        var sellerBalance = await _ledgerService.GetBalanceAsync(sellerId, AccountType.SellerPending, currency);
        sellerBalance.Should().Be(9000L); // 10000 - 10% commission

        var entries = await _db.LedgerEntries
            .Where(e => e.ReferenceId == paymentId.ToString())
            .ToListAsync();
        entries.Should().HaveCount(3); // Seller credit + platform debit + platform revenue credit
    }

    [Fact]
    public async Task PaymentCompletedConsumer_Should_Skip_When_SellerId_Empty()
    {
        // Arrange
        var paymentEvent = new PaymentCompletedEvent
        {
            PaymentId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            SellerId = Guid.Empty, // Empty seller ID
            AmountCents = 10000L,
            Currency = "USD"
        };

        // Act
        await _harness.Bus.Publish(paymentEvent);

        // Assert
        await _harness.Consumed.Any<PaymentCompletedEvent>();

        var entries = await _db.LedgerEntries.ToListAsync();
        entries.Should().BeEmpty(); // No ledger entries should be created
    }

    [Fact]
    public async Task PaymentCompletedConsumer_Should_Be_Idempotent()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var profile = SellerProfile.Create(sellerId);
        profile.CommissionPercentage = 10m;
        _db.SellerProfiles.Add(profile);
        await _db.SaveChangesAsync();

        var paymentEvent = new PaymentCompletedEvent
        {
            PaymentId = paymentId,
            OrderId = orderId,
            SellerId = sellerId,
            AmountCents = 10000L,
            Currency = "USD"
        };

        // Act - publish twice
        await _harness.Bus.Publish(paymentEvent);
        await _harness.Bus.Publish(paymentEvent);

        // Assert
        var consumed = await _harness.Consumed.SelectAsync<PaymentCompletedEvent>().Take(2).ToListAsync();
        consumed.Should().HaveCount(2);

        var sellerBalance = await _ledgerService.GetBalanceAsync(sellerId, AccountType.SellerPending, "USD");
        sellerBalance.Should().Be(9000L); // Should only be credited once

        var entries = await _db.LedgerEntries
            .Where(e => e.ReferenceId == paymentId.ToString())
            .ToListAsync();
        entries.Should().HaveCount(3); // Not 6
    }

    [Fact]
    public async Task RefundIssuedConsumer_Should_Debit_Seller_Account()
    {
        // Arrange - first create a payment
        var sellerId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var amountCents = 10000L;
        var currency = "USD";

        await _ledgerService.CreditSellerAsync(sellerId, amountCents, currency, paymentId, "Test payment");
        await _db.SaveChangesAsync();

        var refundEvent = new RefundIssuedEvent
        {
            PaymentId = paymentId,
            OrderId = orderId,
            AmountCents = amountCents,
            Currency = currency
        };

        // Act
        await _harness.Bus.Publish(refundEvent);

        // Assert
        await _harness.Consumed.Any<RefundIssuedEvent>();

        var sellerBalance = await _ledgerService.GetBalanceAsync(sellerId, AccountType.SellerPending, currency);
        sellerBalance.Should().Be(0L); // Should be reversed to zero
    }

    [Fact]
    public async Task RefundIssuedConsumer_Should_Skip_When_No_Seller_Found()
    {
        // Arrange
        var nonExistentPaymentId = Guid.NewGuid();
        var refundEvent = new RefundIssuedEvent
        {
            PaymentId = nonExistentPaymentId,
            OrderId = Guid.NewGuid(),
            AmountCents = 10000L,
            Currency = "USD"
        };

        // Act
        await _harness.Bus.Publish(refundEvent);

        // Assert
        await _harness.Consumed.Any<RefundIssuedEvent>();

        var entries = await _db.LedgerEntries
            .Where(e => e.ReferenceId.StartsWith("REFUND:"))
            .ToListAsync();
        entries.Should().BeEmpty(); // No refund entries should be created
    }

    [Fact]
    public async Task RefundIssuedConsumer_Should_Be_Idempotent()
    {
        // Arrange - create payment first
        var sellerId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await _ledgerService.CreditSellerAsync(sellerId, 10000L, "USD", paymentId, "Test payment");
        await _db.SaveChangesAsync();

        var refundEvent = new RefundIssuedEvent
        {
            PaymentId = paymentId,
            OrderId = orderId,
            AmountCents = 10000L,
            Currency = "USD"
        };

        // Act - publish twice
        await _harness.Bus.Publish(refundEvent);
        await _harness.Bus.Publish(refundEvent);

        // Assert
        var consumed = await _harness.Consumed.SelectAsync<RefundIssuedEvent>().Take(2).ToListAsync();
        consumed.Should().HaveCount(2);

        var sellerBalance = await _ledgerService.GetBalanceAsync(sellerId, AccountType.SellerPending, "USD");
        sellerBalance.Should().Be(0L); // Should still be zero, not negative

        var refundEntries = await _db.LedgerEntries
            .Where(e => e.ReferenceId.StartsWith("REFUND:"))
            .ToListAsync();
        refundEntries.Should().HaveCount(3); // Only one set of refund entries
    }
}