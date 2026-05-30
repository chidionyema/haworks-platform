using FluentValidation;
using Haworks.BuildingBlocks.Common;

namespace Haworks.Payouts.Application.Ledger.Queries.GetBalance;

internal sealed class GetBalanceQueryValidator : AbstractValidator<GetBalanceQuery>
{
    public GetBalanceQueryValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.Currency).NotEmpty().MustBeValidCurrency();
        RuleFor(x => x.Type).IsInEnum();
    }
}
