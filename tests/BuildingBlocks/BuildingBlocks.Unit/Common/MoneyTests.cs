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
    [InlineData("GBP", true)]
    [InlineData("KWD", true)]   // 3-decimal, real
    [InlineData("BHD", true)]   // 3-decimal, real
    [InlineData("KRW", true)]   // 0-decimal, real
    [InlineData("usd", false)]  // lowercase
    [InlineData("US", false)]   // too short
    [InlineData("USDD", false)] // too long
    [InlineData("US1", false)]  // digit
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidCurrencyCode_ValidatesAgainstIso4217Allowlist(string? code, bool expected) =>
        Money.IsValidCurrencyCode(code).Should().Be(expected);

    // ADV-07: well-formed but NON-EXISTENT codes are now REJECTED by the ISO 4217 allowlist
    // (previously they passed structural validation and silently got exponent 2).
    [Theory]
    [InlineData("ZZZ")]
    [InlineData("QQQ")]
    [InlineData("ABC")]
    [InlineData("XXX")]   // "no currency" — not transactable
    public void IsValidCurrencyCode_RejectsWellFormedNonexistentCodes(string code) =>
        Money.IsValidCurrencyCode(code).Should().BeFalse();

    [Theory]
    [InlineData("USD", 39.99, 3999L)]    // 2-decimal: cents
    [InlineData("EUR", 19.99, 1999L)]    // 2-decimal
    [InlineData("JPY", 4000, 4000L)]     // 0-decimal: no scaling
    [InlineData("KRW", 10000, 10000L)]   // 0-decimal
    [InlineData("VND", 25000, 25000L)]   // 0-decimal
    [InlineData("CLP", 5000, 5000L)]     // 0-decimal
    [InlineData("ISK", 999, 999L)]       // 0-decimal
    [InlineData("KWD", 1.234, 1234L)]    // 3-decimal: fils
    [InlineData("BHD", 1.234, 1234L)]    // 3-decimal
    [InlineData("OMR", 1.234, 1234L)]    // 3-decimal
    public void FromMajorUnits_UsesPerCurrencyExponent(string currency, decimal major, long expectedMinor) =>
        Money.FromMajorUnits(major, currency).MinorUnits.Should().Be(expectedMinor);

    // Rounding is deterministic AwayFromZero (half-up), NOT banker's rounding, at every exponent.
    [Theory]
    [InlineData("USD", 39.995, 4000L)]   // .x5 rounds away from zero -> up
    [InlineData("USD", 2.005, 201L)]     // 200.5 -> 201
    [InlineData("USD", 0.005, 1L)]       // 0.5 cent -> 1
    [InlineData("KWD", 0.0005, 1L)]      // 0.5 fil -> 1 (3-decimal)
    [InlineData("KWD", 1.2345, 1235L)]   // 1234.5 -> 1235
    public void FromMajorUnits_RoundsAwayFromZero(string currency, decimal major, long expectedMinor) =>
        Money.FromMajorUnits(major, currency).MinorUnits.Should().Be(expectedMinor);

    // ADV-08: negatives are ALLOWED, not rejected (no domain-invariant guard). Pin current behavior.
    [Fact]
    public void FromMajorUnits_AllowsNegativeAmounts() =>
        Money.FromMajorUnits(-39.99m, "USD").MinorUnits.Should().Be(-3999L);

    [Theory]
    [InlineData("USD", 2)]
    [InlineData("JPY", 0)]
    [InlineData("KWD", 3)]
    public void GetExponent_MatchesIso4217(string currency, int expected) =>
        Money.GetExponent(currency).Should().Be(expected);

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
