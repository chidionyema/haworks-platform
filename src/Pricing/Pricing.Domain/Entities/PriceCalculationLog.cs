using Haworks.BuildingBlocks.Persistence;

namespace Haworks.Pricing.Domain.Entities;

/// <summary>
/// Append-only audit log for every price calculation. Never updated. Retained for 2 years.
/// </summary>
public sealed class PriceCalculationLog : AuditableEntity
{
    private PriceCalculationLog() { }

    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public long BaseUnitPriceCents { get; private set; }
    public long EffectiveUnitPriceCents { get; private set; }
    public long SubtotalCents { get; private set; }
    public long TaxAmountCents { get; private set; }
    public decimal TaxRateApplied { get; private set; }
    public long TotalCents { get; private set; }
    public string Currency { get; private set; } = "USD";
    public string AppliedRuleIds { get; private set; } = "[]";
    public string? PromotionCodeApplied { get; private set; }
    public DateTimeOffset CalculatedAt { get; private set; }
    public string? UserId { get; private set; }
    public string? CountryCode { get; private set; }
    public string? StateCode { get; private set; }
    public long SnapshotProductPriceCents { get; private set; }

    public static PriceCalculationLog Create(
        Guid productId,
        int quantity,
        long baseUnitPriceCents,
        long effectiveUnitPriceCents,
        long subtotalCents,
        long taxAmountCents,
        decimal taxRateApplied,
        long totalCents,
        string currency,
        string appliedRuleIds,
        string? promotionCodeApplied,
        string? userId,
        string? countryCode,
        string? stateCode,
        long snapshotProductPriceCents)
    {
        return new PriceCalculationLog
        {
            ProductId = productId,
            Quantity = quantity,
            BaseUnitPriceCents = baseUnitPriceCents,
            EffectiveUnitPriceCents = effectiveUnitPriceCents,
            SubtotalCents = subtotalCents,
            TaxAmountCents = taxAmountCents,
            TaxRateApplied = taxRateApplied,
            TotalCents = totalCents,
            Currency = currency,
            AppliedRuleIds = appliedRuleIds,
            PromotionCodeApplied = promotionCodeApplied,
            CalculatedAt = DateTimeOffset.UtcNow,
            UserId = userId,
            CountryCode = countryCode,
            StateCode = stateCode,
            SnapshotProductPriceCents = snapshotProductPriceCents,
        };
    }
}
