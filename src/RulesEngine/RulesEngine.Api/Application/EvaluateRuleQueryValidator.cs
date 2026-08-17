using FluentValidation;

namespace Haworks.RulesEngine.Api.Application;

public class EvaluateRuleQueryValidator : AbstractValidator<EvaluateRuleQuery>
{
    public EvaluateRuleQueryValidator()
    {
        RuleFor(x => x.RuleId).NotEmpty();
        RuleFor(x => x.Inputs)
            .NotNull()
            .Must(inputs => inputs is null || inputs.Count <= 100)
            .WithMessage("Inputs must contain 100 entries or fewer.");
    }
}
