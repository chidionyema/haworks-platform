using FluentValidation;
using Haworks.Catalog.Application.Commands;

namespace Haworks.Catalog.Application.Validators;

internal sealed class DeleteProductReviewCommandValidator : AbstractValidator<DeleteProductReviewCommand>
{
    public DeleteProductReviewCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.ReviewId).NotEmpty();
    }
}
