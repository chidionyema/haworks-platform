using FluentValidation;
using Haworks.BuildingBlocks.Common;
using Haworks.Catalog.Application.Commands.Reservations;

namespace Haworks.Catalog.Application.Validators.Reservations;

public sealed class ConfirmReservationCommandValidator : AbstractValidator<ConfirmReservationCommand>
{
    public ConfirmReservationCommandValidator()
    {
        RuleFor(x => x.ReservationId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.CustomerEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.Currency).NotEmpty().MustBeValidCurrency();
        RuleFor(x => x.TotalAmountCents).GreaterThan(0);
    }
}
