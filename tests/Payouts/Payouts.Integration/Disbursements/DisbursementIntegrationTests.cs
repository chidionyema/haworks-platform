using FluentAssertions;
using Haworks.Payouts.Application.Disbursements.Services;
using Haworks.Payouts.Application.Common.Interfaces;
using Haworks.Payouts.Domain.Aggregates;
using Haworks.Payouts.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Haworks.Payouts.Integration.Disbursements;

[Collection(nameof(PayoutsIntegrationTestDefinition))]
public class DisbursementIntegrationTests : IAsyncLifetime
{
    private readonly PayoutsWebAppFactory _factory;
    private readonly IServiceScope _scope;
    private readonly IDisbursementService _disbursementService;
    private readonly IPayoutsDbContext _context;

    public DisbursementIntegrationTests(PayoutsWebAppFactory factory)
    {
        _factory = factory;
        _scope = _factory.Services.CreateScope();
        _disbursementService = _scope.ServiceProvider.GetRequiredService<IDisbursementService>();
        _context = _scope.ServiceProvider.GetRequiredService<IPayoutsDbContext>();
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureSchemaAsync();
        await _factory.ResetDatabaseAsync();
    }
    public Task DisposeAsync() { _scope.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task ProcessEligiblePayoutsAsync_Should_Execute_Payout_When_Threshold_Met()
    {
        var sellerId = Guid.NewGuid();
        var profile = SellerProfile.Create(sellerId);
        profile.ExternalProviderId = "acct_test";
        profile.PayoutsEnabled = true;
        profile.PayoutThresholdCents = 5000L;
        _context.SellerProfiles.Add(profile);
        var account = LedgerAccount.Create(sellerId, AccountType.SellerPayable, "USD");
        account.UpdateBalance(10000L, EntryType.Credit);
        _context.LedgerAccounts.Add(account);
        await _context.SaveChangesAsync();
        await _disbursementService.ProcessEligiblePayoutsAsync();
        var updatedAccount = await _context.LedgerAccounts.FirstAsync(a => a.Id == account.Id);
        updatedAccount.BalanceCents.Should().Be(0);
        var payout = await _context.Payouts.FirstAsync(p => p.SellerId == sellerId);
        payout.AmountCents.Should().Be(10000L);
        payout.Status.Should().Be(PayoutStatus.InTransit);
    }
}
