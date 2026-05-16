namespace Haworks.Catalog.Api.Models;

public sealed record CreateProductReviewRequest
{
    public required string Title { get; init; }
    public required string Content { get; init; }
    public required int Rating { get; init; }
    public string? AuthorName { get; init; }
}

public sealed record ProductReviewResponse(
    Guid Id,
    Guid ProductId,
    string UserId,
    string? AuthorName,
    string? Title,
    string? Body,
    int Rating,
    bool IsApproved,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
