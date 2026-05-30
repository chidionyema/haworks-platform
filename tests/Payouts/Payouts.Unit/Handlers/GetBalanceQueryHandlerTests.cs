using FluentAssertions;
using Haworks.Payouts.Application.Ledger.Queries.GetBalance;
using Haworks.Payouts.Domain.Aggregates;
using Haworks.Payouts.Domain.Enums;
using Haworks.Payouts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Haworks.Payouts.Unit.Handlers;

public sealed class GetBalanceQueryHandlerTests : IDisposable
{
    private readonly PayoutsDbContext _context;
    private readonly GetBalanceQueryHandler _handler;
    private bool _disposed = false;

    public GetBalanceQueryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<PayoutsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _context = new PayoutsDbContext(options);
        _handler = new GetBalanceQueryHandler(_context);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _context.Dispose();
        }
        _disposed = true;
    }

    [Fact]
    public async Task Handle_Should_Return_Account_Balance_When_Account_Exists()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var accountType = AccountType.SellerPending;
        var currency = "USD";
        var balanceAmount = 5000L;

        var account = LedgerAccount.Create(ownerId, accountType, currency);
        account.UpdateBalance(balanceAmount, EntryType.Credit);

        _context.LedgerAccounts.Add(account);
        await _context.SaveChangesAsync();

        var query = new GetBalanceQuery(ownerId, accountType, currency);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().Be(balanceAmount);
    }

    [Fact]
    public async Task Handle_Should_Return_Zero_When_Account_Does_Not_Exist()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var accountType = AccountType.SellerPending;
        var currency = "USD";

        var query = new GetBalanceQuery(ownerId, accountType, currency);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().Be(0L);
    }

    [Fact]
    public async Task Handle_Should_Return_Zero_When_Account_Has_Zero_Balance()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var accountType = AccountType.SellerPayable;
        var currency = "EUR";

        var account = LedgerAccount.Create(ownerId, accountType, currency);
        // Don't update balance, should remain 0

        _context.LedgerAccounts.Add(account);
        await _context.SaveChangesAsync();

        var query = new GetBalanceQuery(ownerId, accountType, currency);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().Be(0L);
    }

    [Fact]
    public async Task Handle_Should_Distinguish_Between_Different_Account_Types()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var currency = "USD";

        var pendingAccount = LedgerAccount.Create(ownerId, AccountType.SellerPending, currency);
        pendingAccount.UpdateBalance(3000L, EntryType.Credit);

        var payableAccount = LedgerAccount.Create(ownerId, AccountType.SellerPayable, currency);
        payableAccount.UpdateBalance(7000L, EntryType.Credit);

        _context.LedgerAccounts.AddRange(pendingAccount, payableAccount);
        await _context.SaveChangesAsync();

        // Act
        var pendingBalance = await _handler.Handle(new GetBalanceQuery(ownerId, AccountType.SellerPending, currency), CancellationToken.None);
        var payableBalance = await _handler.Handle(new GetBalanceQuery(ownerId, AccountType.SellerPayable, currency), CancellationToken.None);

        // Assert
        pendingBalance.Should().Be(3000L);
        payableBalance.Should().Be(7000L);
    }

    [Fact]
    public async Task Handle_Should_Distinguish_Between_Different_Currencies()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var accountType = AccountType.SellerPending;

        var usdAccount = LedgerAccount.Create(ownerId, accountType, "USD");
        usdAccount.UpdateBalance(1000L, EntryType.Credit);

        var eurAccount = LedgerAccount.Create(ownerId, accountType, "EUR");
        eurAccount.UpdateBalance(2000L, EntryType.Credit);

        _context.LedgerAccounts.AddRange(usdAccount, eurAccount);
        await _context.SaveChangesAsync();

        // Act
        var usdBalance = await _handler.Handle(new GetBalanceQuery(ownerId, accountType, "USD"), CancellationToken.None);
        var eurBalance = await _handler.Handle(new GetBalanceQuery(ownerId, accountType, "EUR"), CancellationToken.None);

        // Assert
        usdBalance.Should().Be(1000L);
        eurBalance.Should().Be(2000L);
    }

    [Fact]
    public async Task Handle_Should_Distinguish_Between_Different_Owners()
    {
        // Arrange
        var owner1Id = Guid.NewGuid();
        var owner2Id = Guid.NewGuid();
        var accountType = AccountType.SellerPending;
        var currency = "USD";

        var account1 = LedgerAccount.Create(owner1Id, accountType, currency);
        account1.UpdateBalance(4000L, EntryType.Credit);

        var account2 = LedgerAccount.Create(owner2Id, accountType, currency);
        account2.UpdateBalance(8000L, EntryType.Credit);

        _context.LedgerAccounts.AddRange(account1, account2);
        await _context.SaveChangesAsync();

        // Act
        var owner1Balance = await _handler.Handle(new GetBalanceQuery(owner1Id, accountType, currency), CancellationToken.None);
        var owner2Balance = await _handler.Handle(new GetBalanceQuery(owner2Id, accountType, currency), CancellationToken.None);

        // Assert
        owner1Balance.Should().Be(4000L);
        owner2Balance.Should().Be(8000L);
    }

    [Theory]
    [InlineData(AccountType.SellerPending)]
    [InlineData(AccountType.SellerPayable)]
    [InlineData(AccountType.PlatformHolding)]
    [InlineData(AccountType.PlatformRevenue)]
    public async Task Handle_Should_Work_With_All_Account_Types(AccountType accountType)
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var currency = "USD";
        var balance = 12345L;

        var account = LedgerAccount.Create(ownerId, accountType, currency);
        account.UpdateBalance(balance, EntryType.Credit);

        _context.LedgerAccounts.Add(account);
        await _context.SaveChangesAsync();

        var query = new GetBalanceQuery(ownerId, accountType, currency);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().Be(balance);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("GBP")]
    [InlineData("JPY")]
    public async Task Handle_Should_Work_With_Different_Currencies(string currency)
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var accountType = AccountType.SellerPending;
        var balance = 9999L;

        var account = LedgerAccount.Create(ownerId, accountType, currency);
        account.UpdateBalance(balance, EntryType.Credit);

        _context.LedgerAccounts.Add(account);
        await _context.SaveChangesAsync();

        var query = new GetBalanceQuery(ownerId, accountType, currency);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().Be(balance);
    }

    [Fact]
    public async Task Handle_Should_Use_AsNoTracking_For_Read_Only_Operation()
    {
        // This test verifies the query is read-only and doesn't track changes
        // Arrange
        var ownerId = Guid.NewGuid();
        var accountType = AccountType.SellerPending;
        var currency = "USD";

        var account = LedgerAccount.Create(ownerId, accountType, currency);
        account.UpdateBalance(5000L, EntryType.Credit);

        _context.LedgerAccounts.Add(account);
        await _context.SaveChangesAsync();

        var query = new GetBalanceQuery(ownerId, accountType, currency);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().Be(5000L);

        // Verify no entities are being tracked after the query
        var trackedEntities = _context.ChangeTracker.Entries().ToList();
        trackedEntities.Should().HaveCount(1); // Only the account we added, not the one from the query
        trackedEntities[0].Entity.Should().BeOfType<LedgerAccount>();
        trackedEntities[0].State.Should().Be(EntityState.Unchanged);
    }
}