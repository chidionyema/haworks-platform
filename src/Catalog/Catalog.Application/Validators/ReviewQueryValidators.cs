using FluentValidation;
using Haworks.Catalog.Application.Queries;

namespace Haworks.Catalog.Application.Validators;

internal sealed class GetProductReviewQueryValidator : AbstractValidator<GetProductReviewQuery>
{
    public GetProductReviewQueryValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.ReviewId).NotEmpty();
    }
}

internal sealed class GetProductReviewsQueryValidator : AbstractValidator<GetProductReviewsQuery>
{
    public GetProductReviewsQueryValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 100);
    }
}

internal sealed class ListProductsQueryValidator : AbstractValidator<ListProductsQuery>
{
    public ListProductsQueryValidator()
    {
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 100);
    }
}
