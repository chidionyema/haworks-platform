using Haworks.Pricing.Domain.Aggregates;
using Haworks.Pricing.Domain.Enums;
using Haworks.Pricing.Application.Commands;

namespace Haworks.Pricing.Application.Promotions;

public sealed class DiscountCalculator : IDiscountCalculator
{
    public PriceQuoteDto CalculateQuote(GetPriceQuoteCommand request, IReadOnlyCollection<Promotion> applicablePromotions)
    {
        var lineDiscounts = request.Lines.Select(l => new CartLineDiscountInternal(l)).ToList();

        foreach (var promo in applicablePromotions)
        {
            ApplyPromotion(lineDiscounts, promo);
        }

        var resultLines = lineDiscounts.Select(l => new CartLineDiscountDto(
            l.ProductId,
            l.TotalDiscount,
            l.FinalPrice)).ToList();

        var totalDiscount = resultLines.Sum(l => l.DiscountAmount);
        var finalPrice = resultLines.Sum(l => l.FinalPrice);

        return new PriceQuoteDto(resultLines, totalDiscount, finalPrice);
    }

    private void ApplyPromotion(List<CartLineDiscountInternal> lines, Promotion promo)
    {
        var targetProductIds = promo.Rules
            .Where(r => r.RuleType == RuleType.SpecificProduct)
            .Select(r => Guid.Parse(r.TargetValue))
            .ToList();

        foreach (var line in lines)
        {
            if (targetProductIds.Count > 0 && !targetProductIds.Contains(line.ProductId))
                continue;

            decimal discount = 0;
            if (promo.DiscountType == DiscountType.PercentOff)
            {
                discount = line.UnitPrice * (promo.DiscountValue / 100);
            }
            else if (promo.DiscountType == DiscountType.FixedAmount)
            {
                if (targetProductIds.Count > 0)
                {
                    discount = promo.DiscountValue;
                }
                else
                {
                    var totalValue = lines.Sum(l => l.TotalPrice);
                    if (totalValue > 0)
                    {
                        discount = (line.TotalPrice / totalValue) * promo.DiscountValue / line.Quantity;
                    }
                }
            }

            line.AddDiscount(discount * line.Quantity);
        }
    }

    private sealed class CartLineDiscountInternal
    {
        public Guid ProductId { get; }
        public int Quantity { get; }
        public decimal UnitPrice { get; }
        public decimal TotalPrice => UnitPrice * Quantity;
        public decimal TotalDiscount { get; private set; }
        public decimal FinalPrice => Math.Max(0, TotalPrice - TotalDiscount);

        public CartLineDiscountInternal(CartLineDto dto)
        {
            ProductId = dto.ProductId;
            Quantity = dto.Quantity;
            UnitPrice = dto.UnitPrice;
        }

        public void AddDiscount(decimal amount)
        {
            TotalDiscount += amount;
        }
    }
}
