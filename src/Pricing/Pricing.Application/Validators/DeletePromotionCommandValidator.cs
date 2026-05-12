using FluentValidation;
using Haworks.Pricing.Application.Commands;

namespace Haworks.Pricing.Application.Validators;

public sealed class DeletePromotionCommandValidator : AbstractValidator<DeletePromotionCommand>
{
    public DeletePromotionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
