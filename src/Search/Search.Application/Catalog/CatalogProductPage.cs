namespace Haworks.Search.Application.Catalog;

/// <summary>
/// Catalog's offset-paginated list response. Mirrors
/// <c>Haworks.BuildingBlocks.Common.PagedResult&lt;ProductDto&gt;</c>.
/// </summary>
public sealed record CatalogProductPage
{
    public IReadOnlyList<CatalogProductDto> Items { get; init; } = [];
    public int Total { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; }
}
