using Haworks.Pricing.Domain.Aggregates;
using Haworks.Pricing.Application.Commands;

namespace Haworks.Pricing.Application.Promotions;

public interface IPromotionRepository
{
    Task<IReadOnlyCollection<Promotion>> GetActivePromotionsAsync(CancellationToken ct = default);
}

public interface IPromotionResolver
{
    Task<IReadOnlyCollection<Promotion>> ResolveApplicablePromotionsAsync(GetPriceQuoteCommand request, CancellationToken ct = default);
}

public interface IDiscountCalculator
{
    PriceQuoteDto CalculateQuote(GetPriceQuoteCommand request, IReadOnlyCollection<Promotion> applicablePromotions);
}
