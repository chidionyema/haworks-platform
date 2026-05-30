using FluentValidation;
using Haworks.Catalog.Application.Commands;
using Haworks.BuildingBlocks.Common;

namespace Haworks.Catalog.Application.Validators;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotNull().MaximumLength(4000);
        RuleFor(x => x.UnitPriceCents).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().MustBeValidCurrency();
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.InitialStock).GreaterThanOrEqualTo(0);
    }
}
