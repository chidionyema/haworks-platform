using FluentValidation;
using Haworks.Identity.Application.Commands.Auth;

namespace Haworks.Identity.Application.Validators.Auth;

public sealed class CreateServiceTokenCommandValidator : AbstractValidator<CreateServiceTokenCommand>
{
    public CreateServiceTokenCommandValidator()
    {
        // No properties to validate in the current version of the command,
        // but the architectural guard requires a validator to be present.
    }
}
