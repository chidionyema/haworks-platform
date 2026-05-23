using FluentAssertions;
using Haworks.Payouts.Domain.Aggregates;
using Haworks.Payouts.Domain.Enums;
using Xunit;

namespace Haworks.Payouts.Unit.Domain;

public class LedgerEntryTests
{
    [Fact]
    public void Create_with_valid_params_succeeds()
    {
        var entry = LedgerEntry.Create(Guid.NewGuid(), Guid.NewGuid(), 10000L, EntryType.Credit, "Payment", "ref-1");
        entry.AmountCents.Should().Be(10000L);
    }

    [Fact]
    public void Create_with_zero_amount_throws()
    {
        var act = () => LedgerEntry.Create(Guid.NewGuid(), Guid.NewGuid(), 0L, EntryType.Credit, "test", "ref");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_with_negative_amount_throws()
    {
        var act = () => LedgerEntry.Create(Guid.NewGuid(), Guid.NewGuid(), -500L, EntryType.Credit, "test", "ref");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_with_empty_accountId_throws()
    {
        var act = () => LedgerEntry.Create(Guid.Empty, Guid.NewGuid(), 10000L, EntryType.Credit, "test", "ref");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_with_empty_description_throws()
    {
        var act = () => LedgerEntry.Create(Guid.NewGuid(), Guid.NewGuid(), 10000L, EntryType.Credit, "", "ref");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_with_empty_referenceId_throws()
    {
        var act = () => LedgerEntry.Create(Guid.NewGuid(), Guid.NewGuid(), 10000L, EntryType.Credit, "test", "");
        act.Should().Throw<ArgumentException>();
    }
}
