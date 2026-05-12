using Haworks.Pricing.Domain.Aggregates;
using Haworks.Pricing.Domain.Enums;
using Haworks.Pricing.Application.Commands;

namespace Haworks.Pricing.Application.Promotions;

public sealed class PromotionResolver : IPromotionResolver
{
    private readonly IPromotionRepository _repo;

    public PromotionResolver(IPromotionRepository repo)
    {
        _repo = repo;
    }

    public async Task<IReadOnlyCollection<Promotion>> ResolveApplicablePromotionsAsync(GetPriceQuoteCommand request, CancellationToken ct = default)
    {
        var activePromotions = await _repo.GetActivePromotionsAsync(ct);
        var applicable = new List<Promotion>();

        foreach (var promo in activePromotions)
        {
            if (IsApplicable(promo, request))
            {
                applicable.Add(promo);
            }
        }

        return applicable;
    }

    private bool IsApplicable(Promotion promo, GetPriceQuoteCommand request)
    {
        if (promo.Rules.Count == 0) return true;

        foreach (var rule in promo.Rules)
        {
            if (!EvaluateRule(rule, request)) return false;
        }

        return true;
    }

    private bool EvaluateRule(PromotionRule rule, GetPriceQuoteCommand request)
    {
        return rule.RuleType switch
        {
            RuleType.MinimumOrderValue => EvaluateMinOrderValue(rule, request),
            RuleType.SpecificProduct => EvaluateSpecificProduct(rule, request),
            RuleType.Category => true,
            _ => false
        };
    }

    private bool EvaluateMinOrderValue(PromotionRule rule, GetPriceQuoteCommand request)
    {
        if (decimal.TryParse(rule.TargetValue, out var minValue))
        {
            var totalValue = request.Lines.Sum(l => l.UnitPrice * l.Quantity);
            return totalValue >= minValue;
        }
        return false;
    }

    private bool EvaluateSpecificProduct(PromotionRule rule, GetPriceQuoteCommand request)
    {
        if (Guid.TryParse(rule.TargetValue, out var productId))
        {
            return request.Lines.Any(l => l.ProductId == productId);
        }
        return false;
    }
}
