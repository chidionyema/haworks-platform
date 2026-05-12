using FluentValidation;

namespace Haworks.Payments.Application.Commands.Refunds;

public sealed class CreateRefundCommandValidator : AbstractValidator<CreateRefundCommand>
{
    public CreateRefundCommandValidator()
    {
        RuleFor(x => x.TransactionId).NotEmpty();
        RuleFor(x => x.AmountCents).GreaterThan(0).When(x => x.AmountCents.HasValue);
    }
}
