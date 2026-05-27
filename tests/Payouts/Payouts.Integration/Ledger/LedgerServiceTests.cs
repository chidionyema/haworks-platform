using FluentAssertions;
using Haworks.Payouts.Application.Ledger.Services;
using Haworks.Payouts.Domain.Aggregates;
using Haworks.Payouts.Domain.Enums;
using Haworks.Payouts.Domain.Aggregates;
using Haworks.Payouts.Infrastructure.Persistence;
using Haworks.BuildingBlocks.Testing.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Haworks.Payouts.Integration.Ledger;

public sealed class LedgerServiceTests : IAsyncLifetime
{
    private PayoutsDbContext _context = null!;
    private LedgerService _service = null!;

    public async Task InitializeAsync()
    {
        _connString = await SharedTestPostgres.CreateDatabaseAsync("payouts-ledger-unit");
        var connString = _connString;
        var options = new DbContextOptionsBuilder<PayoutsDbContext>()
            .UseNpgsql(connString)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;
        _context = new PayoutsDbContext(options);

        var schema = _context.Model.GetDefaultSchema();
        if (!string.IsNullOrEmpty(schema))
        {
#pragma warning disable HWK027, EF1002
            await _context.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS {schema};");
#pragma warning restore HWK027, EF1002
        }
        await _context.Database.MigrateAsync();

        var seller = SellerProfile.Create(_sellerId);
        _context.SellerProfiles.Add(seller);
        await _context.SaveChangesAsync();

        _service = new LedgerService(_context, NullLogger<LedgerService>.Instance);
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    private string _connString = null!;

    private PayoutsDbContext CreateFreshContext()
    {
        var options = new DbContextOptionsBuilder<PayoutsDbContext>()
            .UseNpgsql(_connString)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new PayoutsDbContext(options);
    }

    private static readonly Guid _sellerId = Guid.NewGuid();
    private static readonly Guid _orderId = Guid.NewGuid();

    [Fact]
    public async Task CreditSellerAsync_creates_three_entries()
    {
        var refId = Guid.NewGuid();
        await using var ctx = CreateFreshContext();
        var svc = new LedgerService(ctx, NullLogger<LedgerService>.Instance);
        await svc.CreditSellerAsync(_sellerId, 10000, "USD", refId, "test", CancellationToken.None);

        await using var qctx = CreateFreshContext();
        var entries = await qctx.LedgerEntries.Where(e => e.ReferenceId == refId.ToString()).ToListAsync();
        entries.Should().HaveCount(3);
    }

    [Fact]
    public async Task CreditSellerAsync_is_idempotent_on_same_referenceId()
    {
        var refId = Guid.NewGuid();
        await _service.CreditSellerAsync(_sellerId, 5000, "USD", refId, "idem-test", CancellationToken.None);
        await _service.CreditSellerAsync(_sellerId, 5000, "USD", refId, "idem-test", CancellationToken.None);
        var entries = await _context.LedgerEntries.Where(e => e.ReferenceId == refId.ToString()).ToListAsync();
        entries.Should().HaveCount(3);
    }

    [Fact]
    public async Task DebitSellerAsync_reverses_credit()
    {
        var rcId = Guid.NewGuid();
        var rdId = Guid.NewGuid();
        await _service.CreditSellerAsync(_sellerId, 10000, "USD", rcId, "credit", CancellationToken.None);
        await _service.DebitSellerAsync(_sellerId, 10000, "USD", rdId, "debit", CancellationToken.None);
        var account = await _context.LedgerAccounts.FirstOrDefaultAsync(a => a.OwnerId == _sellerId && a.Currency == "USD");
        account.Should().NotBeNull();
        account!.BalanceCents.Should().Be(0);
    }

    [Fact]
    public async Task DebitSellerAsync_is_idempotent()
    {
        var rcId2 = Guid.NewGuid();
        var rdId2 = Guid.NewGuid();
        await _service.CreditSellerAsync(_sellerId, 10000, "USD", rcId2, "credit", CancellationToken.None);
        await _service.DebitSellerAsync(_sellerId, 5000, "USD", rdId2, "debit", CancellationToken.None);
        await _service.DebitSellerAsync(_sellerId, 5000, "USD", rdId2, "debit", CancellationToken.None);
        var debits = await _context.LedgerEntries.Where(e => e.ReferenceId == rdId2.ToString()).ToListAsync();
        debits.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreditSellerAsync_deducts_commission()
    {
        var rComm = Guid.NewGuid();
        await _service.CreditSellerAsync(_sellerId, 10000, "USD", rComm, "comm-test", CancellationToken.None);
        var commission = await _context.LedgerEntries.FirstOrDefaultAsync(e => e.ReferenceId == rComm.ToString() && e.Type == EntryType.Debit);
        commission.Should().NotBeNull();
        commission!.AmountCents.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CreditSellerAsync_with_zero_amount_throws()
    {
        var act = () => _service.CreditSellerAsync(_sellerId, 0, "USD", Guid.NewGuid(), "ref-zero", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
