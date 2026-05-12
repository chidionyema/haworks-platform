using FluentAssertions;
using Haworks.Pricing.Application.Commands;
using Haworks.Pricing.Application.Promotions;
using Haworks.Pricing.Domain.Aggregates;
using Haworks.Pricing.Domain.Enums;
using Xunit;

namespace Haworks.Pricing.Unit.Promotions;

public sealed class DiscountCalculatorTests
{
    private readonly DiscountCalculator _calculator;

    public DiscountCalculatorTests()
    {
        _calculator = new DiscountCalculator();
    }

    [Fact]
    public void CalculateQuote_applies_PercentOff_to_all_lines()
    {
        var now = DateTime.UtcNow;
        var promo = Promotion.Create("10% Off", "D", DiscountType.PercentOff, 10, now.AddDays(-1), now.AddDays(1));
        
        var request = new GetPriceQuoteCommand(new[]
        {
            new CartLineDto(Guid.NewGuid(), 2, 100), // 200 -> 180 (20 discount)
            new CartLineDto(Guid.NewGuid(), 1, 50)   // 50 -> 45 (5 discount)
        });

        var quote = _calculator.CalculateQuote(request, new[] { promo });

        quote.TotalDiscount.Should().Be(25);
        quote.FinalPrice.Should().Be(225);
        quote.Lines.Should().HaveCount(2);
    }

    [Fact]
    public void CalculateQuote_applies_PercentOff_to_specific_product()
    {
        var now = DateTime.UtcNow;
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var promo = Promotion.Create("10% Off P1", "D", DiscountType.PercentOff, 10, now.AddDays(-1), now.AddDays(1));
        promo.AddRule(RuleType.SpecificProduct, p1.ToString());
        
        var request = new GetPriceQuoteCommand(new[]
        {
            new CartLineDto(p1, 1, 100), // 100 -> 90 (10 discount)
            new CartLineDto(p2, 1, 100)  // 100 -> 100 (0 discount)
        });

        var quote = _calculator.CalculateQuote(request, new[] { promo });

        quote.TotalDiscount.Should().Be(10);
        quote.FinalPrice.Should().Be(190);
    }

    [Fact]
    public void CalculateQuote_applies_FixedAmount_split_across_lines()
    {
        var now = DateTime.UtcNow;
        var promo = Promotion.Create("$30 Off", "D", DiscountType.FixedAmount, 30, now.AddDays(-1), now.AddDays(1));
        
        var request = new GetPriceQuoteCommand(new[]
        {
            new CartLineDto(Guid.NewGuid(), 1, 100), // 1/3 of total -> 10 discount
            new CartLineDto(Guid.NewGuid(), 1, 200)  // 2/3 of total -> 20 discount
        });

        var quote = _calculator.CalculateQuote(request, new[] { promo });

        quote.TotalDiscount.Should().Be(30);
        quote.FinalPrice.Should().Be(270);
    }
}
