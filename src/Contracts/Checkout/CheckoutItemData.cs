namespace Haworks.Contracts.Checkout;

/// <summary>
/// Represents an item in the checkout for event serialization.
/// </summary>
public sealed record CheckoutItemData
{
    /// <summary>The product being purchased.</summary>
    public required Guid ProductId { get; init; }

    /// <summary>Product name for display purposes.</summary>
    public required string ProductName { get; init; }

    /// <summary>Quantity being purchased.</summary>
    public required int Quantity { get; init; }

    /// <summary>Unit price at time of checkout in minor currency units (cents).</summary>
    public required long UnitPriceCents { get; init; }

    /// <summary>ISO 4217 currency code (e.g., "USD", "EUR").</summary>
    public required string Currency { get; init; }
}
