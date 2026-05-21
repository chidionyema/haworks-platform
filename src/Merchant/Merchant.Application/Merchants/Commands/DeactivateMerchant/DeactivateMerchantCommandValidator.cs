using FluentValidation;

namespace Haworks.Merchant.Application.Merchants.Commands.DeactivateMerchant;

internal sealed class DeactivateMerchantCommandValidator : AbstractValidator<DeactivateMerchantCommand>
{
    public DeactivateMerchantCommandValidator()
    {
        RuleFor(x => x.MerchantId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}
