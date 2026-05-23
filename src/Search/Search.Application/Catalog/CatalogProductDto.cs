namespace Haworks.Search.Application.Catalog;

/// <summary>
/// Mirror of <c>Haworks.Catalog.Application.DTOs.ProductDto</c>. Search-svc
/// is not allowed to project across the contract boundary directly, so the
/// shape is duplicated here. <see cref="CategoryName"/> is null on the list
/// projection — only <c>GET /api/products/{id}</c> populates it. The
/// indexer enriches per-product via the get-by-id endpoint accordingly.
/// </summary>
public sealed record CatalogProductDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public long UnitPriceCents { get; init; }
    public int StockQuantity { get; init; }
    public bool IsInStock { get; init; }
    public bool IsListed { get; init; }
    public Guid CategoryId { get; init; }
    public string? CategoryName { get; init; }
}
