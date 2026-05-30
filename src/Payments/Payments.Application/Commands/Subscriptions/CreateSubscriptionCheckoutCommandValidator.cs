using FluentValidation;
using Haworks.BuildingBlocks.Common;

namespace Haworks.Payments.Application.Commands.Subscriptions;

public sealed class CreateSubscriptionCheckoutCommandValidator : AbstractValidator<CreateSubscriptionCheckoutCommand>
{
    public CreateSubscriptionCheckoutCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.PriceId).NotEmpty();
        RuleFor(x => x.AmountCents).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().MustBeValidCurrency();
    }
}
