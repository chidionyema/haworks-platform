using FluentValidation;
using Haworks.Orders.Application.Commands;
using Haworks.BuildingBlocks.Common;

namespace Haworks.Orders.Application.Validators;

public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.CustomerEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.TotalAmountCents).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().MustBeValidCurrency();
        RuleFor(x => x.SagaId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.ProductName).NotEmpty();
            item.RuleFor(i => i.Quantity).GreaterThan(0);
            item.RuleFor(i => i.UnitPriceCents).GreaterThan(0);
        });
    }
}
