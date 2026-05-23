using FluentAssertions;
using Haworks.Payouts.Domain.Aggregates;
using Haworks.Payouts.Domain.Enums;
using Xunit;

namespace Haworks.Payouts.Unit.Domain;

public class LedgerAccountTests
{
    [Fact]
    public void Create_sets_zero_balance()
    {
        var account = LedgerAccount.Create(Guid.NewGuid(), AccountType.SellerPending, "USD");
        account.BalanceCents.Should().Be(0);
    }

    [Fact]
    public void Credit_increases_balance()
    {
        var account = LedgerAccount.Create(Guid.NewGuid(), AccountType.SellerPending, "USD");
        account.UpdateBalance(10000L, EntryType.Credit);
        account.BalanceCents.Should().Be(10000L);
    }

    [Fact]
    public void Debit_decreases_balance()
    {
        var account = LedgerAccount.Create(Guid.NewGuid(), AccountType.SellerPending, "USD");
        account.UpdateBalance(10000L, EntryType.Credit);
        account.UpdateBalance(4000L, EntryType.Debit);
        account.BalanceCents.Should().Be(6000L);
    }

    [Fact]
    public void Debit_exceeding_seller_balance_throws()
    {
        var account = LedgerAccount.Create(Guid.NewGuid(), AccountType.SellerPending, "USD");
        account.UpdateBalance(5000L, EntryType.Credit);
        var act = () => account.UpdateBalance(5100L, EntryType.Debit);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Insufficient*");
    }

    [Fact]
    public void Debit_on_platform_account_requires_sufficient_balance()
    {
        var account = LedgerAccount.Create(Guid.NewGuid(), AccountType.PlatformHolding, "USD");
        account.UpdateBalance(10000L, EntryType.Credit);
        account.UpdateBalance(5000L, EntryType.Debit);
        account.BalanceCents.Should().Be(5000L);
    }
}
