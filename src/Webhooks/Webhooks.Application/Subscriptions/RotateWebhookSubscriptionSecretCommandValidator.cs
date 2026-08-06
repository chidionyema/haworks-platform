using FluentValidation;

namespace Haworks.Webhooks.Application.Subscriptions;

internal sealed class RotateWebhookSubscriptionSecretCommandValidator : AbstractValidator<RotateWebhookSubscriptionSecretCommand>
{
    public RotateWebhookSubscriptionSecretCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Secret).MaximumLength(200).When(x => x.Secret != null);
    }
}
