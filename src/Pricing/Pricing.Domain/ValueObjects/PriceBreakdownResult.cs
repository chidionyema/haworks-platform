namespace Haworks.Pricing.Domain.ValueObjects;

/// <summary>
/// Result of a price calculation — the full breakdown returned by the engine.
/// </summary>
public sealed record PriceBreakdownResult
{
    public required Guid CalculationId { get; init; }
    public required Guid ProductId { get; init; }
    public required int Quantity { get; init; }
    public required string Currency { get; init; }
    public required long BaseUnitPriceCents { get; init; }
    public required long EffectiveUnitPriceCents { get; init; }
    public required IReadOnlyList<AppliedDiscount> Discounts { get; init; }
    public required long SubtotalCents { get; init; }
    public required long TaxAmountCents { get; init; }
    public required decimal TaxRate { get; init; }
    public required long TotalCents { get; init; }
    public string? PromoCodeApplied { get; init; }
    public required DateTimeOffset SnapshotAt { get; init; }
}

/// <summary>
/// A single discount that was applied during calculation.
/// </summary>
public sealed record AppliedDiscount
{
    public required string Type { get; init; }
    public required string Label { get; init; }
    public required long AmountOffCents { get; init; }
    public decimal? Pct { get; init; }
}
