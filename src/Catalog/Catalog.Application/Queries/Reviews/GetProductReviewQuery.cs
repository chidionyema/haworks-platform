using Haworks.BuildingBlocks.Common;
using Haworks.Catalog.Application.DTOs;
using Haworks.Catalog.Domain.Interfaces;
using MediatR;

namespace Haworks.Catalog.Application.Queries;

public sealed record GetProductReviewQuery(
    Guid ProductId,
    Guid ReviewId,
    string? UserId,
    bool IsAdmin
) : IRequest<Result<ProductReviewDto>>;

internal sealed class GetProductReviewQueryHandler
    : IRequestHandler<GetProductReviewQuery, Result<ProductReviewDto>>
{
    private readonly IProductReviewRepository _repository;

    public GetProductReviewQueryHandler(
        IProductReviewRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<ProductReviewDto>> Handle(
        GetProductReviewQuery request,
        CancellationToken cancellationToken)
    {
        var review = await _repository.GetByIdAsync(request.ReviewId, cancellationToken);
        if (review == null)
        {
            return Result.Failure<ProductReviewDto>(new Error("Reviews.NotFound", "Review not found"));
        }

        if (review.ProductId != request.ProductId)
        {
            return Result.Failure<ProductReviewDto>(new Error("Reviews.InvalidProduct", "Review does not belong to this product"));
        }

        // Only show approved reviews to non-admins unless it's their own review
        if (!review.IsApproved && !request.IsAdmin && !string.Equals(review.UserId, request.UserId, StringComparison.Ordinal))
        {
            return Result.Failure<ProductReviewDto>(new Error("Reviews.NotFound", "Review not found"));
        }

        var reviewDto = new ProductReviewDto(
            review.Id,
            review.ProductId,
            review.UserId,
            review.AuthorName,
            review.Title,
            review.Body,
            review.Rating,
            review.IsApproved,
            review.CreatedAt,
            review.LastModifiedDate);

        return Result.Success(reviewDto);
    }
}
