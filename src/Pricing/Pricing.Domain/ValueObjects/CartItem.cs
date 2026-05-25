namespace Haworks.Pricing.Domain.ValueObjects;

/// <summary>
/// Represents a single item in a pricing calculation request.
/// </summary>
public sealed record CartItem
{
    public required Guid ProductId { get; init; }
    public required string ProductName { get; init; }
    public required int Quantity { get; init; }
    public required long CatalogUnitPriceCents { get; init; }
    public Guid? CategoryId { get; init; }
}
