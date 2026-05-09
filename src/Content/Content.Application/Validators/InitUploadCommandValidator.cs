using FluentValidation;
using Haworks.Content.Application.Commands;

namespace Haworks.Content.Application.Validators;

internal sealed class InitUploadCommandValidator : AbstractValidator<InitUploadCommand>
{
    public InitUploadCommandValidator()
    {
        RuleFor(x => x.EntityId).NotEmpty();
        RuleFor(x => x.EntityType).NotEmpty().MaximumLength(64);
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(127);
        RuleFor(x => x.TotalSize).GreaterThan(0).LessThanOrEqualTo(50L * 1024 * 1024 * 1024);
        RuleFor(x => x.OwnerUserId).NotEmpty();
    }
}
