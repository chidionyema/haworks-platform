using FluentValidation;
using Haworks.Pricing.Application.Queries;

namespace Haworks.Pricing.Application.Validators;

public sealed class ListPromotionsQueryValidator : AbstractValidator<ListPromotionsQuery>
{
    public ListPromotionsQueryValidator()
    {
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).GreaterThan(0).LessThanOrEqualTo(100);
    }
}
