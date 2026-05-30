using FluentValidation;
using Haworks.BuildingBlocks.Common;

namespace Haworks.Payments.Application.Commands.Refunds;

public sealed class CreateRefundCommandValidator : AbstractValidator<CreateRefundCommand>
{
    public CreateRefundCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.AmountCents).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().MustBeValidCurrency();
    }
}
