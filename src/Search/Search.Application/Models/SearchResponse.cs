namespace Haworks.Search.Application.Models;

/// <summary>
/// Public response envelope for <c>GET /search</c>. Field shape and casing
/// pinned to spec §3.1 — JsonSerializerDefaults.Web (camelCase) is the
/// project default, so the property names below serialize as documented.
///
/// <see cref="SearchHitResponse.Score"/> and <see cref="SearchHitResponse.Snippet"/>
/// are placeholders in v1: score = 1.0 for every hit, snippet = name. v2
/// will populate them from Meilisearch's <c>showRankingScore</c> and
/// <c>attributesToHighlight</c> features (already supported by the engine,
/// just not wired through ISearchIndex yet). The fields exist now so the
/// BFF + future Gemini-side re-ranker bind to a stable shape.
/// </summary>
public sealed record SearchResponse
{
    public required string Query { get; init; }
    public Guid? CategoryId { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalHits { get; init; }
    public required long TookMs { get; init; }
    public required IReadOnlyList<SearchHitResponse> Hits { get; init; }
}

public sealed record SearchHitResponse
{
    public required string ProductId { get; init; }
    public required string Name { get; init; }
    public required string Snippet { get; init; }
    public required string CategoryId { get; init; }
    public required string CategoryName { get; init; }
    public required decimal UnitPrice { get; init; }
    public required string Currency { get; init; }
    public required bool IsInStock { get; init; }
    public required double Score { get; init; }
}
