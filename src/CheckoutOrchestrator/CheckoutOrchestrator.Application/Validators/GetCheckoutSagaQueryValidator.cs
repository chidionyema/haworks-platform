using FluentValidation;

namespace Haworks.CheckoutOrchestrator.Application.Queries;

public sealed class GetCheckoutSagaQueryValidator : AbstractValidator<GetCheckoutSagaQuery>
{
    public GetCheckoutSagaQueryValidator()
    {
        RuleFor(x => x.SagaId).NotEmpty();
    }
}

public sealed class GetCheckoutSagaByOrderIdQueryValidator : AbstractValidator<GetCheckoutSagaByOrderIdQuery>
{
    public GetCheckoutSagaByOrderIdQueryValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}
