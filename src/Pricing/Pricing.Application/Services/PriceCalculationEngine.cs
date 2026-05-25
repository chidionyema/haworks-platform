using System.Text.Json;
using Haworks.Pricing.Domain.Entities;
using Haworks.Pricing.Domain.Enums;
using Haworks.Pricing.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Haworks.Pricing.Application.Services;

/// <summary>
/// Pure calculation engine. No database, no HTTP — just math.
/// Registered as Singleton (no scoped dependencies).
/// </summary>
public sealed class PriceCalculationEngine
{
    private readonly ILogger<PriceCalculationEngine> _logger;

    public PriceCalculationEngine(ILogger<PriceCalculationEngine> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Calculates effective price for a product given rules, tiers, and optional promotion.
    /// Returns a PriceBreakdownResult with all discount details.
    /// </summary>
    public PriceBreakdownResult Calculate(
        Guid productId,
        int quantity,
        long baseUnitPriceCents,
        string currency,
        Guid? categoryId,
        IReadOnlyList<PriceRule> rules,
        PromotionCode? promotionCode,
        DateTimeOffset now)
    {
        var discounts = new List<AppliedDiscount>();
        var appliedRuleIds = new List<Guid>();
        var effectiveUnitPriceCents = baseUnitPriceCents;

        // 1. Sort rules by Priority DESC, then specificity (ProductId > CategoryId)
        var applicableRules = rules
            .Where(r => r.IsApplicableTo(productId, categoryId, quantity, now))
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.ProductId.HasValue ? 2 : r.CategoryId.HasValue ? 1 : 0)
            .ToList();

        // 2. Check TieredPrices first
        foreach (var rule in applicableRules)
        {
            var matchingTier = rule.TieredPrices
                .FirstOrDefault(t => t.ContainsQuantity(quantity));

            if (matchingTier is not null)
            {
                var tierDiscountCents = effectiveUnitPriceCents - matchingTier.UnitPriceCents;
                if (tierDiscountCents > 0)
                {
                    var pct = baseUnitPriceCents > 0
                        ? Math.Round((decimal)tierDiscountCents / baseUnitPriceCents * 100m, 2, MidpointRounding.AwayFromZero)
                        : 0m;

                    discounts.Add(new AppliedDiscount
                    {
                        Type = "TieredVolume",
                        Label = $"Buy {matchingTier.FromQuantity}+ get tiered price",
                        AmountOffCents = tierDiscountCents,
                        Pct = pct,
                    });
                    appliedRuleIds.Add(rule.Id);
                }
                effectiveUnitPriceCents = matchingTier.UnitPriceCents;
                break; // Only one tier applies
            }
        }

        // 3. Apply remaining rules in priority order
        foreach (var rule in applicableRules)
        {
            if (appliedRuleIds.Contains(rule.Id)) continue; // Already applied via tier

            switch (rule.DiscountType)
            {
                case DiscountType.Percentage:
                    var pctOffCents = (long)(effectiveUnitPriceCents * rule.DiscountPercentage / 100m);
                    effectiveUnitPriceCents -= pctOffCents;
                    discounts.Add(new AppliedDiscount
                    {
                        Type = "Percentage",
                        Label = $"{rule.DiscountPercentage}% off",
                        AmountOffCents = pctOffCents,
                        Pct = rule.DiscountPercentage,
                    });
                    appliedRuleIds.Add(rule.Id);
                    break;

                case DiscountType.FixedAmount:
                    var fixedOffCents = Math.Min(rule.DiscountAmountCents, effectiveUnitPriceCents);
                    effectiveUnitPriceCents -= fixedOffCents;
                    discounts.Add(new AppliedDiscount
                    {
                        Type = "FixedAmount",
                        Label = $"{rule.DiscountAmountCents}c off",
                        AmountOffCents = fixedOffCents,
                    });
                    appliedRuleIds.Add(rule.Id);
                    break;

                case DiscountType.FreeShipping:
                    // Not applied to price calculation in v1
                    _logger.LogDebug("FreeShipping rule {RuleId} skipped in v1", rule.Id);
                    break;

                default:
                    _logger.LogWarning("Unknown DiscountType {Type} on rule {RuleId}, skipping", rule.DiscountType, rule.Id);
                    break;
            }

            // Floor at zero
            effectiveUnitPriceCents = Math.Max(0, effectiveUnitPriceCents);
        }

        // 4. Calculate subtotal (integer arithmetic, exact)
        var subtotalCents = effectiveUnitPriceCents * quantity;

        // 5. Apply promotion code to subtotal (not per-unit)
        var promoApplied = false;
        if (promotionCode is not null && promotionCode.CanRedeem(now))
        {
            switch (promotionCode.DiscountType)
            {
                case DiscountType.Percentage:
                    var promoOffCents = (long)(subtotalCents * promotionCode.DiscountPercentage / 100m);
                    subtotalCents -= promoOffCents;
                    discounts.Add(new AppliedDiscount
                    {
                        Type = "PromotionCode",
                        Label = promotionCode.Code,
                        AmountOffCents = promoOffCents,
                        Pct = promotionCode.DiscountPercentage,
                    });
                    promoApplied = true;
                    break;

                case DiscountType.FixedAmount:
                    var promoFixedOffCents = Math.Min(promotionCode.DiscountAmountCents, subtotalCents);
                    subtotalCents -= promoFixedOffCents;
                    discounts.Add(new AppliedDiscount
                    {
                        Type = "PromotionCode",
                        Label = promotionCode.Code,
                        AmountOffCents = promoFixedOffCents,
                    });
                    promoApplied = true;
                    break;
            }

            subtotalCents = Math.Max(0, subtotalCents);
        }

        return new PriceBreakdownResult
        {
            CalculationId = Guid.NewGuid(),
            ProductId = productId,
            Quantity = quantity,
            Currency = currency,
            BaseUnitPriceCents = baseUnitPriceCents,
            EffectiveUnitPriceCents = effectiveUnitPriceCents,
            Discounts = discounts,
            SubtotalCents = subtotalCents,
            TaxAmountCents = 0L, // Filled in by caller after tax calculation
            TaxRate = 0m,        // Filled in by caller
            TotalCents = subtotalCents, // Updated after tax
            PromoCodeApplied = promoApplied ? promotionCode!.Code : null,
            SnapshotAt = now,
        };
    }
}
