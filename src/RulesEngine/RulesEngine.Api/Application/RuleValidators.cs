using FluentValidation;

namespace Haworks.RulesEngine.Api.Application;

public sealed class DeleteRuleCommandValidator : AbstractValidator<DeleteRuleCommand>
{
    public DeleteRuleCommandValidator()
    {
        RuleFor(x => x.Id).NotEqual(Guid.Empty);
    }
}

public sealed class GetRuleQueryValidator : AbstractValidator<GetRuleQuery>
{
    public GetRuleQueryValidator()
    {
        RuleFor(x => x.Id).NotEqual(Guid.Empty);
    }
}

public sealed class ListRulesQueryValidator : AbstractValidator<ListRulesQuery>
{
    public ListRulesQueryValidator()
    {
        // No strict rules for list query filter, but validator must exist.
    }
}
