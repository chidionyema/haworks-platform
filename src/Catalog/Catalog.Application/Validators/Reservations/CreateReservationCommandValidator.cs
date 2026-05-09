using FluentValidation;
using Haworks.Catalog.Application.Commands.Reservations;

namespace Haworks.Catalog.Application.Validators.Reservations;

public sealed class CreateReservationCommandValidator : AbstractValidator<CreateReservationCommand>
{
    public CreateReservationCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Items)
            .NotNull()
            .NotEmpty()
            .WithMessage("At least one reservation item is required.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEqual(Guid.Empty);
            item.RuleFor(i => i.Quantity).GreaterThan(0);
        });
    }
}
