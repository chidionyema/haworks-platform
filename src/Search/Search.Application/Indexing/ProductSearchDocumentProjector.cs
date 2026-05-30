using Haworks.Search.Application.Models;

namespace Haworks.Search.Application.Indexing;

/// <summary>
/// Maps a catalog ProductDto to the Meilisearch document shape.
/// Pure function — no IO, no state — so it's unit-testable in isolation.
/// </summary>
public static class ProductSearchDocumentProjector
{
    public static ProductSearchDocument From(
        Guid id,
        string name,
        string description,
        long unitPriceCents,
        string currencyCode,
        bool isInStock,
        bool isListed,
        Guid categoryId,
        string? categoryName,
        long sourceVersion)
        => new()
        {
            ProductIdKey = id.ToString("N"),
            ProductId = id.ToString(),
            Name = name ?? "",
            Description = description ?? "",
            CategoryId = categoryId.ToString(),
            CategoryName = string.IsNullOrEmpty(categoryName) ? "Uncategorized" : categoryName,
            UnitPriceCents = unitPriceCents,
            CurrencyCode = currencyCode,
            IsInStock = isInStock,
            IsListed = isListed,
            SourceVersion = sourceVersion,
            IndexedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
}
