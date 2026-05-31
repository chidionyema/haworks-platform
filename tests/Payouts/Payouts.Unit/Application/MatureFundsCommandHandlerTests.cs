using FluentAssertions;
using Haworks.Payouts.Application.Ledger.Commands.MatureFunds;
using Haworks.Payouts.Domain.Aggregates;
using Haworks.Payouts.Domain.Enums;
using Haworks.Payouts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Haworks.Payouts.Unit.Application;

public sealed class MatureFundsCommandHandlerTests : IDisposable
{
    private readonly PayoutsDbContext _context;
    private readonly MatureFundsCommandHandler _handler;

    public MatureFundsCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<PayoutsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _context = new PayoutsDbContext(options);
        _handler = new MatureFundsCommandHandler(_context, NullLogger<MatureFundsCommandHandler>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task Handle_Should_Mature_Pending_Funds_To_Payable()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var currency = "USD";
        var amountCents = 10000L;

        // Create pending account with balance
        var pendingAccount = LedgerAccount.Create(sellerId, AccountType.SellerPending, currency);
        pendingAccount.UpdateBalance(amountCents, EntryType.Credit);
        _context.LedgerAccounts.Add(pendingAccount);
        await _context.SaveChangesAsync();

        var command = new MatureFundsCommand("test-batch-123");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var accounts = await _context.LedgerAccounts
            .Where(a => a.OwnerId == sellerId)
            .ToListAsync();

        var pendingAccountAfter = accounts.First(a => a.Type == AccountType.SellerPending);
        var payableAccount = accounts.First(a => a.Type == AccountType.SellerPayable);

        pendingAccountAfter.BalanceCents.Should().Be(0L);
        payableAccount.BalanceCents.Should().Be(amountCents);

        // Verify ledger entries created
        var entries = await _context.LedgerEntries.ToListAsync();
        entries.Should().HaveCount(2); // One debit from pending, one credit to payable

        var debitEntry = entries.First(e => e.Type == EntryType.Debit);
        var creditEntry = entries.First(e => e.Type == EntryType.Credit);

        debitEntry.AmountCents.Should().Be(amountCents);
        creditEntry.AmountCents.Should().Be(amountCents);
        debitEntry.Description.Should().Be("Funds matured");
        creditEntry.Description.Should().Be("Funds matured");
    }

    [Fact]
    public async Task Handle_Should_Create_Payable_Account_If_Not_Exists()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var currency = "USD";
        var amountCents = 5000L;

        // Only create pending account, no payable account exists
        var pendingAccount = LedgerAccount.Create(sellerId, AccountType.SellerPending, currency);
        pendingAccount.UpdateBalance(amountCents, EntryType.Credit);
        _context.LedgerAccounts.Add(pendingAccount);
        await _context.SaveChangesAsync();

        var command = new MatureFundsCommand();

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var payableAccounts = await _context.LedgerAccounts
            .Where(a => a.OwnerId == sellerId && a.Type == AccountType.SellerPayable)
            .ToListAsync();

        payableAccounts.Should().HaveCount(1);
        payableAccounts[0].BalanceCents.Should().Be(amountCents);
    }

    [Fact]
    public async Task Handle_Should_Process_Multiple_Sellers_In_Single_Batch()
    {
        // Arrange
        var seller1Id = Guid.NewGuid();
        var seller2Id = Guid.NewGuid();
        var currency = "USD";

        var pending1 = LedgerAccount.Create(seller1Id, AccountType.SellerPending, currency);
        pending1.UpdateBalance(10000L, EntryType.Credit);
        var pending2 = LedgerAccount.Create(seller2Id, AccountType.SellerPending, currency);
        pending2.UpdateBalance(15000L, EntryType.Credit);

        _context.LedgerAccounts.AddRange(pending1, pending2);
        await _context.SaveChangesAsync();

        var command = new MatureFundsCommand();

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var seller1Payable = await _context.LedgerAccounts
            .FirstAsync(a => a.OwnerId == seller1Id && a.Type == AccountType.SellerPayable);
        var seller2Payable = await _context.LedgerAccounts
            .FirstAsync(a => a.OwnerId == seller2Id && a.Type == AccountType.SellerPayable);

        seller1Payable.BalanceCents.Should().Be(10000L);
        seller2Payable.BalanceCents.Should().Be(15000L);
    }

    [Fact]
    public async Task Handle_Should_Do_Nothing_When_No_Pending_Funds()
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        // Create pending account with zero balance
        var pendingAccount = LedgerAccount.Create(sellerId, AccountType.SellerPending, "USD");
        _context.LedgerAccounts.Add(pendingAccount);
        await _context.SaveChangesAsync();

        var command = new MatureFundsCommand();

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var entries = await _context.LedgerEntries.ToListAsync();
        entries.Should().BeEmpty(); // No entries should be created
    }

    [Fact]
    public async Task Handle_Should_Generate_Unique_Batch_References()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var pendingAccount = LedgerAccount.Create(sellerId, AccountType.SellerPending, "USD");
        pendingAccount.UpdateBalance(10000L, EntryType.Credit);
        _context.LedgerAccounts.Add(pendingAccount);
        await _context.SaveChangesAsync();

        // Act - run twice
        await _handler.Handle(new MatureFundsCommand(), CancellationToken.None);

        // Add more pending funds for second run
        pendingAccount.UpdateBalance(5000L, EntryType.Credit);
        await _context.SaveChangesAsync();

        await _handler.Handle(new MatureFundsCommand(), CancellationToken.None);

        // Assert
        var entries = await _context.LedgerEntries.ToListAsync();
        var references = entries.Select(e => e.ReferenceId).Where(r => r.StartsWith("MATURITY:")).Distinct().ToList();

        references.Should().HaveCount(4); // 2 entries per run × 2 runs = 4 unique references
        references.Should().AllSatisfy(r => r.Should().Match("MATURITY:*"));
    }

    [Fact]
    public async Task Handle_Should_Handle_Multiple_Currencies()
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        var pendingUsd = LedgerAccount.Create(sellerId, AccountType.SellerPending, "USD");
        pendingUsd.UpdateBalance(10000L, EntryType.Credit);
        var pendingEur = LedgerAccount.Create(sellerId, AccountType.SellerPending, "EUR");
        pendingEur.UpdateBalance(8000L, EntryType.Credit);

        _context.LedgerAccounts.AddRange(pendingUsd, pendingEur);
        await _context.SaveChangesAsync();

        var command = new MatureFundsCommand();

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var payableAccounts = await _context.LedgerAccounts
            .Where(a => a.OwnerId == sellerId && a.Type == AccountType.SellerPayable)
            .ToListAsync();

        payableAccounts.Should().HaveCount(2);
        payableAccounts.First(a => a.Currency == "USD").BalanceCents.Should().Be(10000L);
        payableAccounts.First(a => a.Currency == "EUR").BalanceCents.Should().Be(8000L);
    }
}