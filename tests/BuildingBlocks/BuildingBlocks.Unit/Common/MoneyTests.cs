using FluentAssertions;
using Haworks.BuildingBlocks.Common;
using Xunit;

namespace Haworks.BuildingBlocks.Unit.Common;

public sealed class MoneyTests
{
    [Theory]
    [InlineData("USD", true)]
    [InlineData("EUR", true)]
    [InlineData("JPY", true)]
    [InlineData("usd", false)]
    [InlineData("US", false)]
    [InlineData("USDD", false)]
    [InlineData("US1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidCurrencyCode_ValidatesIso4217Format(string? code, bool expected) =>
        Money.IsValidCurrencyCode(code).Should().Be(expected);

    [Theory]
    [InlineData("USD", 39.99, 3999L)]   // 2-decimal: cents
    [InlineData("JPY", 4000, 4000L)]    // 0-decimal: no scaling
    [InlineData("KWD", 1.234, 1234L)]   // 3-decimal: fils
    public void FromMajorUnits_UsesPerCurrencyExponent(string currency, decimal major, long expectedMinor) =>
        Money.FromMajorUnits(major, currency).MinorUnits.Should().Be(expectedMinor);

    [Fact]
    public void FromMajorUnits_RejectsInvalidCurrency() =>
        FluentActions.Invoking(() => Money.FromMajorUnits(10m, "usd"))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void FromMajorUnits_RejectsOverflow() =>
        FluentActions.Invoking(() => Money.FromMajorUnits(decimal.MaxValue, "USD"))
            .Should().Throw<OverflowException>();

    [Fact]
    public void TryFromMajorUnits_ReturnsFalseForInvalidCurrency()
    {
        Money.TryFromMajorUnits(10m, "us", out _).Should().BeFalse();
    }

    [Fact]
    public void TryFromMajorUnits_ReturnsFalseForOverflow()
    {
        Money.TryFromMajorUnits(decimal.MaxValue, "USD", out _).Should().BeFalse();
    }

    [Fact]
    public void TryFromMajorUnits_ReturnsTrueAndConvertsForValidInput()
    {
        Money.TryFromMajorUnits(39.99m, "USD", out var money).Should().BeTrue();
        money.MinorUnits.Should().Be(3999L);
        money.CurrencyCode.Should().Be("USD");
    }

    [Theory]
    [InlineData(3999L, "USD", 39.99)]
    [InlineData(4000L, "JPY", 4000)]
    [InlineData(1234L, "KWD", 1.234)]
    public void ToMajorUnits_RoundTripsPerCurrency(long minor, string currency, decimal expectedMajor) =>
        new Money(minor, currency).ToMajorUnits().Should().Be(expectedMajor);
}
