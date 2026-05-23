using FluentAssertions;
using Haworks.Payouts.Application.Common.Interfaces;
using Haworks.Payouts.Application.Ledger.Services;
using Haworks.Payouts.Domain.Enums;
using Haworks.Payouts.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haworks.Payouts.Integration.Ledger;

[Collection(nameof(PayoutsIntegrationTestDefinition))]
public class LedgerIntegrationTests : IAsyncLifetime
{
    private readonly PayoutsWebAppFactory _factory;
    private readonly IServiceScope _scope;
    private readonly ILedgerService _ledgerService;
    private readonly PayoutsDbContext _db;

    public LedgerIntegrationTests(PayoutsWebAppFactory factory)
    {
        _factory = factory;
        _scope = _factory.Services.CreateScope();
        _ledgerService = _scope.ServiceProvider.GetRequiredService<ILedgerService>();
        _db = _scope.ServiceProvider.GetRequiredService<PayoutsDbContext>();
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureSchemaAsync();
        await _factory.ResetDatabaseAsync();
    }
    public Task DisposeAsync() { _scope.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task CreditSellerAsync_Should_Create_Double_Entry_And_Update_Balances()
    {
        var sellerId = Guid.NewGuid();
        var amountCents = 10000L;
        var currency = "USD";
        await _ledgerService.CreditSellerAsync(sellerId, amountCents, currency, Guid.NewGuid(), "Test credit");
        await _db.SaveChangesAsync(); // outbox handles this in production
        var sellerBalance = await _ledgerService.GetBalanceAsync(sellerId, AccountType.SellerPending, currency);
        sellerBalance.Should().Be(9000L); // 10000 - 10% default commission
        var platformId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var platformBalance = await _ledgerService.GetBalanceAsync(platformId, AccountType.PlatformHolding, currency);
        platformBalance.Should().Be(amountCents);
    }

    [Fact]
    public async Task CreditSellerAsync_deducts_commission()
    {
        var sellerId = Guid.NewGuid();
        var amountCents = 20000L;
        var currency = "USD";
        await _ledgerService.CreditSellerAsync(sellerId, amountCents, currency, Guid.NewGuid(), "Commission test");
        await _db.SaveChangesAsync(); // outbox handles this in production

        var sellerBalance = await _ledgerService.GetBalanceAsync(sellerId, AccountType.SellerPending, currency);
        sellerBalance.Should().Be(18000L); // 20000 - 10% default commission = 18000

        var platformId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var revenueBalance = await _ledgerService.GetBalanceAsync(platformId, AccountType.PlatformRevenue, currency);
        revenueBalance.Should().BeGreaterThanOrEqualTo(2000L); // 10% of 20000; shared platform account accumulates across tests
    }
}
